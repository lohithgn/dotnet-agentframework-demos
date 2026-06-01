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

using System.ClientModel;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
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
// This example needs the RediSearch module, so run the Redis Stack image:
//   podman run --rm -d --name redis-stack -p 6379:6379 -p 8001:8001 redis/redis-stack:latest
string redisUrl = Environment.GetEnvironmentVariable("REDIS_URL") ?? "localhost:6379";

IChatClient chatClient = CreateChatClient(apiHost);

AnsiConsole.MarkupLine("\n[bold]=== Agent with Redis Memory (RedisMemoryProvider) ===[/]");

// Verify Redis connectivity up front with a clear message if it is unreachable.
using var redis = await ConnectRedisAsync(redisUrl);

// A context provider stores conversational facts in Redis and retrieves the most
// relevant ones (BM25 full-text search via RediSearch) on each new invocation,
// injecting them into the model's context. The scope (application/agent/user) keeps
// one user's memories isolated from another's.
string userId = Guid.NewGuid().ToString("N");

JsonSerializerOptions ToolJsonOptions =
    new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

var memoryProvider = await RedisMemoryProvider.CreateAsync(
    redis,
    indexName: "agent_memory",
    prefix: "agent_memory",
    applicationId: "weather_app",
    agentId: "weather_agent",
    userId: userId);

AIAgent agent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    ChatOptions = new()
    {
        Instructions =
            "You are a helpful weather assistant. Personalize replies using provided context. " +
            "Before answering, always check for stored context.",
        Tools = [AIFunctionFactory.Create(GetWeather, serializerOptions: ToolJsonOptions)],
    },
    Name = "WeatherAgent",
    AIContextProviders = [memoryProvider],
});

// Each turn runs on a fresh session, so the memory provider — not chat history — is
// what carries information from one turn to the next.

// Step 1: Teach the agent a user preference.
AnsiConsole.MarkupLine("\n[bold]--- Step 1: Teaching a preference ---[/]");
AnsiConsole.MarkupLine("[blue]User:[/] Remember that my favorite city is Tokyo.");
var response = await agent.RunAsync("Remember that my favorite city is Tokyo.");
AnsiConsole.MarkupLine($"[green]Agent:[/] {Markup.Escape(response.Text)}");

// Step 2: Ask the agent to recall the preference from memory.
AnsiConsole.MarkupLine("\n[bold]--- Step 2: Recalling a preference ---[/]");
AnsiConsole.MarkupLine("[blue]User:[/] What's my favorite city?");
response = await agent.RunAsync("What's my favorite city?");
AnsiConsole.MarkupLine($"[green]Agent:[/] {Markup.Escape(response.Text)}");

// Step 3: Use a tool, then verify the agent remembers the tool output details.
AnsiConsole.MarkupLine("\n[bold]--- Step 3: Tool use with memory ---[/]");
AnsiConsole.MarkupLine("[blue]User:[/] What's the weather in Paris?");
response = await agent.RunAsync("What's the weather in Paris?");
AnsiConsole.MarkupLine($"[green]Agent:[/] {Markup.Escape(response.Text)}");

AnsiConsole.MarkupLine("\n[blue]User:[/] What city did I just ask about and what was the weather?");
response = await agent.RunAsync("What city did I just ask about and what was the weather?");
AnsiConsole.MarkupLine($"[green]Agent:[/] {Markup.Escape(response.Text)}");

[Description("Returns weather data for a given city.")]
static WeatherReport GetWeather(
    [Description("The city to get the weather for.")] string city)
{
    AnsiConsole.MarkupLine($"[grey]Getting weather for {Markup.Escape(city)}[/]");
    string[] conditions = ["sunny", "cloudy", "rainy", "stormy"];
    return new WeatherReport(
        conditions[Random.Shared.Next(conditions.Length)],
        Random.Shared.Next(10, 30));
}

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
            "[yellow]Ensure Redis Stack is running, e.g. 'podman run --rm -d --name redis-stack -p 6379:6379 redis/redis-stack:latest'.[/]");
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

record WeatherReport(string Conditions, int HighCelsius);

/// <summary>
/// A custom <see cref="AIContextProvider"/> modeled on the official Redis context provider.
/// It stores conversation messages as Redis hashes indexed by RediSearch and, on each
/// invocation, retrieves the most relevant ones with a scoped BM25 full-text search, injecting
/// them into the model's context. Memories are scoped by application/agent/user (and, when a
/// session is present, by thread/conversation). The index persists across runs unless
/// <c>overwriteIndex</c> is set, matching the official provider's <c>overwrite_index=false</c>.
/// </summary>
sealed class RedisMemoryProvider : AIContextProvider
{
    private const string DefaultContextPrompt =
        "## Memories\nConsider the following memories when answering user questions:";
    private const int DefaultNumResults = 10;

    private readonly IDatabase _database;
    private readonly string _indexName;
    private readonly string _prefix;
    private readonly string? _applicationId;
    private readonly string? _agentId;
    private readonly string? _userId;
    private readonly string _contextPrompt;

    private RedisMemoryProvider(
        IDatabase database, string indexName, string prefix,
        string? applicationId, string? agentId, string? userId, string contextPrompt)
    {
        _database = database;
        _indexName = indexName;
        _prefix = prefix;
        _applicationId = applicationId;
        _agentId = agentId;
        _userId = userId;
        _contextPrompt = contextPrompt;
    }

    /// <summary>
    /// Creates the provider and ensures the RediSearch index exists. By default the index is
    /// reused if it already exists (matching the official provider's <c>overwrite_index=false</c>);
    /// pass <paramref name="overwriteIndex"/> to drop and rebuild it.
    /// </summary>
    public static async Task<RedisMemoryProvider> CreateAsync(
        IConnectionMultiplexer connection,
        string indexName = "context",
        string prefix = "context",
        string? applicationId = null,
        string? agentId = null,
        string? userId = null,
        string? contextPrompt = null,
        bool overwriteIndex = false)
    {
        ValidateFilters(applicationId, agentId, userId);

        var database = connection.GetDatabase();

        try
        {
            bool indexExists = await IndexExistsAsync(database, indexName);

            if (overwriteIndex && indexExists)
            {
                await database.ExecuteAsync("FT.DROPINDEX", indexName, "DD");
                indexExists = false;
            }

            if (!indexExists)
            {
                await database.ExecuteAsync(
                    "FT.CREATE", indexName,
                    "ON", "HASH",
                    "PREFIX", "1", $"{prefix}:",
                    "SCHEMA",
                    "role", "TAG",
                    "mime_type", "TAG",
                    "content", "TEXT",
                    "conversation_id", "TAG",
                    "message_id", "TAG",
                    "author_name", "TAG",
                    "application_id", "TAG",
                    "agent_id", "TAG",
                    "user_id", "TAG",
                    "thread_id", "TAG");
            }
        }
        catch (RedisServerException ex) when (ex.Message.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine(
                "[red]RediSearch (FT.* commands) is not available on this Redis server.[/]");
            AnsiConsole.MarkupLine(
                "[yellow]Use the Redis Stack image: 'podman run --rm -d --name redis-stack -p 6379:6379 redis/redis-stack:latest'.[/]");
            throw;
        }

        return new RedisMemoryProvider(
            database, indexName, prefix, applicationId, agentId, userId,
            contextPrompt ?? DefaultContextPrompt);
    }

    /// <summary>Retrieve scoped context from Redis and add it to the invocation context.</summary>
    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        ValidateFilters(_applicationId, _agentId, _userId);

        var inputText = string.Join(
            '\n',
            (context.AIContext.Messages ?? [])
                .Where(m => !string.IsNullOrWhiteSpace(m.Text))
                .Select(m => m.Text!.Trim()));

        if (string.IsNullOrWhiteSpace(inputText))
        {
            return new AIContext();
        }

        var memories = await SearchAsync(inputText);
        if (memories.Count == 0)
        {
            return new AIContext();
        }

        var lineSeparatedMemories = string.Join('\n', memories);

        return new AIContext
        {
            Messages =
            [
                new ChatMessage(ChatRole.User, $"{_contextPrompt}\n{lineSeparatedMemories}"),
            ],
        };
    }

    /// <summary>Store request and response messages to Redis for future retrieval.</summary>
    protected override async ValueTask StoreAIContextAsync(
        InvokedContext context, CancellationToken cancellationToken = default)
    {
        ValidateFilters(_applicationId, _agentId, _userId);

        // The demo runs without a session, so there is no thread/conversation id to scope by.
        string? sessionId = null;

        var messagesToStore = context.RequestMessages.Concat(context.ResponseMessages ?? []);

        foreach (var message in messagesToStore)
        {
            string role = message.Role.Value;
            if (string.IsNullOrWhiteSpace(message.Text) ||
                (role != "user" && role != "assistant" && role != "system"))
            {
                continue;
            }

            var entries = new List<HashEntry>
            {
                new("role", role),
                new("content", message.Text),
            };

            AddIfPresent(entries, "message_id", message.MessageId);
            AddIfPresent(entries, "author_name", message.AuthorName);
            AddIfPresent(entries, "application_id", _applicationId);
            AddIfPresent(entries, "agent_id", _agentId);
            AddIfPresent(entries, "user_id", _userId);
            AddIfPresent(entries, "conversation_id", sessionId);
            AddIfPresent(entries, "thread_id", sessionId);

            var key = $"{_prefix}:{Guid.NewGuid():N}";
            await _database.HashSetAsync(key, entries.ToArray());
        }
    }

    // Runs a scoped BM25 full-text search and returns the matching "content" values.
    private async Task<List<string>> SearchAsync(string text)
    {
        var terms = ExtractTerms(text);
        if (terms.Count == 0)
        {
            return [];
        }

        var filters = new List<string>();
        if (!string.IsNullOrEmpty(_applicationId))
            filters.Add($"@application_id:{{{_applicationId}}}");
        if (!string.IsNullOrEmpty(_agentId))
            filters.Add($"@agent_id:{{{_agentId}}}");
        if (!string.IsNullOrEmpty(_userId))
            filters.Add($"@user_id:{{{_userId}}}");

        var contentMatch = $"@content:({string.Join('|', terms)})";
        var query = filters.Count > 0
            ? $"({string.Join(' ', filters)}) {contentMatch}"
            : contentMatch;

        RedisResult result = await _database.ExecuteAsync(
            "FT.SEARCH", _indexName, query,
            "SCORER", "BM25STD",
            "LIMIT", "0", DefaultNumResults,
            "RETURN", "6", "content", "role", "application_id", "agent_id", "user_id", "thread_id",
            "DIALECT", "2");

        return ParseContents(result);
    }

    private static async Task<bool> IndexExistsAsync(IDatabase database, string indexName)
    {
        try
        {
            await database.ExecuteAsync("FT.INFO", indexName);
            return true;
        }
        catch (RedisServerException ex) when (ex.Message.Contains("Unknown index", StringComparison.OrdinalIgnoreCase)
                                              || ex.Message.Contains("no such index", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
    }

    private static void AddIfPresent(List<HashEntry> entries, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            entries.Add(new HashEntry(name, value));
        }
    }

    // Validates that at least one of the application/agent/user scope filters is provided.
    private static void ValidateFilters(string? applicationId, string? agentId, string? userId)
    {
        if (string.IsNullOrEmpty(applicationId) &&
            string.IsNullOrEmpty(agentId) &&
            string.IsNullOrEmpty(userId))
        {
            throw new InvalidOperationException(
                "At least one of the filters application_id, agent_id, or user_id is required.");
        }
    }

    // Extracts lowercase alphanumeric search terms (length >= 3) for the content match.
    private static List<string> ExtractTerms(string text)
    {
        var terms = new List<string>();
        foreach (var raw in text.Split(
            [' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':', '\'', '"', '(', ')'],
            StringSplitOptions.RemoveEmptyEntries))
        {
            var term = new string(raw.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (term.Length >= 3 && !terms.Contains(term))
            {
                terms.Add(term);
            }
        }

        return terms;
    }

    // Parses the "content" field out of an FT.SEARCH reply:
    // [ total, docId1, [ field, value, ... ], docId2, [ field, value, ... ], ... ]
    private static List<string> ParseContents(RedisResult result)
    {
        var contents = new List<string>();
        var items = (RedisResult[])result!;

        for (int i = 1; i + 1 < items.Length; i += 2)
        {
            var fields = (RedisResult[])items[i + 1]!;
            for (int j = 0; j + 1 < fields.Length; j += 2)
            {
                if ((string)fields[j]! == "content")
                {
                    contents.Add((string)fields[j + 1]!);
                }
            }
        }

        return contents;
    }
}
