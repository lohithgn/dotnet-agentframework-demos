#:sdk Microsoft.NET.Sdk
#:package Microsoft.Agents.AI@1.6.2
#:package Microsoft.Agents.AI.OpenAI@1.6.2
#:package Azure.AI.OpenAI@2.9.0-beta.1
#:package Azure.Identity@1.21.0
#:package OpenAI@2.10.0
#:package DotNetEnv@3.2.0
#:package Spectre.Console@0.55.2
#:package StackExchange.Redis@2.8.41
#:property NoWarn=IL2026;IL3050

// Note: Do not add tools to agents using history providers — it causes duplicate
// item errors with the Responses API. See https://github.com/microsoft/agent-framework/issues/3295

using System.ClientModel;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Identity;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using Spectre.Console;
using StackExchange.Redis;

Env.Load();

string apiHost = Environment.GetEnvironmentVariable("API_HOST") ?? "azure";

// StackExchange.Redis expects a "host:port" config string, not a "redis://" URL.
// Start Redis locally with: podman run --rm -d --name maf-redis -p 6379:6379 redis:7-alpine
string redisUrl = Environment.GetEnvironmentVariable("REDIS_URL") ?? "localhost:6379";

IChatClient chatClient = CreateChatClient(apiHost);

// A Redis-backed history provider persists chat messages in an external Redis
// instance, so a conversation survives an application restart.
const string Instructions = "You are a helpful assistant that remembers our conversation.";

AnsiConsole.MarkupLine("\n[bold]=== Persistent Redis Session ===[/]");

// Verify Redis connectivity up front with a clear message if it is unreachable.
using var redis = await ConnectRedisAsync(redisUrl);

// Phase 1: Start a conversation backed by a Redis history provider.
AnsiConsole.MarkupLine("\n[bold]--- Phase 1: Starting conversation ---[/]");

var provider1 = new RedisChatHistoryProvider(redis);

AIAgent agent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    ChatOptions = new() { Instructions = Instructions },
    Name = "MemoryAgent",
    ChatHistoryProvider = provider1,
});

AgentSession session = await agent.CreateSessionAsync();
AnsiConsole.MarkupLine($"[grey]Session stored under Redis key: {provider1.GetSessionRedisKey(session)}[/]");

AnsiConsole.MarkupLine("[blue]User:[/] Hello! My name is Alice and I love hiking.");
var response = await agent.RunAsync("Hello! My name is Alice and I love hiking.", session);
AnsiConsole.MarkupLine($"[green]Agent:[/] {Markup.Escape(response.Text)}");

AnsiConsole.MarkupLine("\n[blue]User:[/] What are some good trails in Colorado?");
response = await agent.RunAsync("What are some good trails in Colorado?", session);
AnsiConsole.MarkupLine($"[green]Agent:[/] {Markup.Escape(response.Text)}");

// The serialized session only carries the Redis key — the messages themselves live
// in Redis. Saving this is how an app would persist a session across restarts.
JsonElement serializedSession = await agent.SerializeSessionAsync(session);

// Phase 2: Simulate an application restart by opening a fresh provider and agent,
// then resuming the same session from its serialized state.
AnsiConsole.MarkupLine("\n[bold]--- Phase 2: Resuming after 'restart' ---[/]");

var provider2 = new RedisChatHistoryProvider(redis);

AIAgent agent2 = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    ChatOptions = new() { Instructions = Instructions },
    Name = "MemoryAgent",
    ChatHistoryProvider = provider2,
});

AgentSession resumedSession = await agent2.DeserializeSessionAsync(serializedSession);
AnsiConsole.MarkupLine($"[grey]Resumed Redis key: {provider2.GetSessionRedisKey(resumedSession)}[/]");

AnsiConsole.MarkupLine("[blue]User:[/] What do you remember about me?");
response = await agent2.RunAsync("What do you remember about me?", resumedSession);
AnsiConsole.MarkupLine($"[green]Agent:[/] {Markup.Escape(response.Text)}");

static async Task<ConnectionMultiplexer> ConnectRedisAsync(string redisUrl)
{
    try
    {
        return await ConnectionMultiplexer.ConnectAsync(redisUrl);
    }
    catch (RedisConnectionException ex)
    {
        AnsiConsole.MarkupLine($"[red]Cannot connect to Redis at {Markup.Escape(redisUrl)}: {Markup.Escape(ex.Message)}[/]");
        AnsiConsole.MarkupLine(
            "[yellow]Ensure Redis is running, e.g. 'podman run --rm -d --name maf-redis -p 6379:6379 redis:7-alpine'.[/]");
        throw;
    }
}

static IChatClient CreateChatClient(string apiHost)
{
    return apiHost switch
    {
        // Note: using AzureCliCredential for local demo runs. For production, prefer
        // ManagedIdentityCredential. See README for details.
        "azure" => new AzureOpenAIClient(
                new Uri(Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")!),
                new AzureCliCredential())
            .GetChatClient(Environment.GetEnvironmentVariable("AZURE_OPENAI_CHAT_DEPLOYMENT")!)
            .AsIChatClient(),

        "openai" => new OpenAIClient(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!)
            .GetChatClient(Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini")
            .AsIChatClient(),

        "ollama" => new OpenAIClient(
                new ApiKeyCredential(Environment.GetEnvironmentVariable("OLLAMA_API_KEY") ?? "nokeyneeded"),
                new OpenAIClientOptions { Endpoint = new Uri(Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT")!) })
            .GetChatClient(Environment.GetEnvironmentVariable("OLLAMA_MODEL")!)
            .AsIChatClient(),

        _ => throw new InvalidOperationException($"Unknown API_HOST: {apiHost}")
    };
}

/// <summary>
/// A custom <see cref="ChatHistoryProvider"/> that persists chat messages in Redis —
/// external-service persistence without a first-party MAF Redis package. Each session's
/// messages are stored in a Redis list (one serialized <see cref="ChatMessage"/> per
/// element, in order). The per-session key is held in the <see cref="AgentSession"/>
/// state bag, so it round-trips automatically when the session is serialized and resumed.
/// </summary>
sealed class RedisChatHistoryProvider : ChatHistoryProvider
{
    private const string KeyPrefix = "redis_chat";

    private readonly ProviderSessionState<string> _sessionState;
    private readonly IDatabase _database;
    private IReadOnlyList<string>? _stateKeys;

    public RedisChatHistoryProvider(IConnectionMultiplexer connection, string? stateKey = null)
    {
        _sessionState = new ProviderSessionState<string>(
            _ => $"{KeyPrefix}:{Guid.NewGuid():N}",
            stateKey ?? nameof(RedisChatHistoryProvider));

        _database = connection.GetDatabase();
    }

    public override IReadOnlyList<string> StateKeys => _stateKeys ??= [_sessionState.StateKey];

    public string GetSessionRedisKey(AgentSession session) =>
        _sessionState.GetOrInitializeState(session);

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        var sessionRedisKey = _sessionState.GetOrInitializeState(context.Session);

        RedisValue[] entries = await _database.ListRangeAsync(sessionRedisKey);

        var messages = new List<ChatMessage>(entries.Length);
        foreach (var entry in entries)
        {
            messages.Add(JsonSerializer.Deserialize<ChatMessage>(
                (string)entry!, AIJsonUtilities.DefaultOptions)!);
        }

        return messages;
    }

    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context, CancellationToken cancellationToken = default)
    {
        var sessionRedisKey = _sessionState.GetOrInitializeState(context.Session);
        var newMessages = context.RequestMessages.Concat(context.ResponseMessages ?? []);

        foreach (var message in newMessages)
        {
            await _database.ListRightPushAsync(
                sessionRedisKey,
                JsonSerializer.Serialize(message, AIJsonUtilities.DefaultOptions));
        }
    }
}
