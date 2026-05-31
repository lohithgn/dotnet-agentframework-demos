#:sdk Microsoft.NET.Sdk
#:package Microsoft.Agents.AI@1.6.2
#:package Microsoft.Agents.AI.OpenAI@1.6.2
#:package Azure.AI.OpenAI@2.9.0-beta.1
#:package Azure.Identity@1.21.0
#:package OpenAI@2.10.0
#:package DotNetEnv@3.2.0
#:package Spectre.Console@0.55.2
#:package Microsoft.Data.Sqlite@10.0.8
#:property NoWarn=IL2026;IL3050

// Note: Do not add tools to agents using history providers — it causes duplicate
// item errors with the Responses API. See https://github.com/microsoft/agent-framework/issues/3295

using System.ClientModel;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Identity;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using OpenAI;
using Spectre.Console;

Env.Load();

string apiHost = Environment.GetEnvironmentVariable("API_HOST") ?? "azure";

IChatClient chatClient = CreateChatClient(apiHost);

// A SQLite-backed history provider persists chat messages to a local file, so a
// conversation survives an application restart without an external service.
const string DbPath = "chat_history.sqlite3";
const string Instructions = "You are a helpful assistant that remembers our conversation.";

AnsiConsole.MarkupLine("\n[bold]=== Persistent SQLite Session ===[/]");

// Phase 1: Start a conversation backed by a SQLite history provider.
AnsiConsole.MarkupLine("\n[bold]--- Phase 1: Starting conversation ---[/]");

using var provider1 = new SqliteChatHistoryProvider(DbPath);

AIAgent agent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    ChatOptions = new() { Instructions = Instructions },
    Name = "MemoryAgent",
    ChatHistoryProvider = provider1,
});

AgentSession session = await agent.CreateSessionAsync();
AnsiConsole.MarkupLine($"[grey]Session stored under SQLite key: {provider1.GetSessionDbKey(session)}[/]");

AnsiConsole.MarkupLine("[blue]User:[/] Hello! My name is Alice and I love hiking.");
var response = await agent.RunAsync("Hello! My name is Alice and I love hiking.", session);
AnsiConsole.MarkupLine($"[green]Agent:[/] {Markup.Escape(response.Text)}");

AnsiConsole.MarkupLine("\n[blue]User:[/] What are some good trails in Colorado?");
response = await agent.RunAsync("What are some good trails in Colorado?", session);
AnsiConsole.MarkupLine($"[green]Agent:[/] {Markup.Escape(response.Text)}");

// The serialized session only carries the SQLite key — the messages themselves live
// in the database. Saving this is how an app would persist a session across restarts.
JsonElement serializedSession = await agent.SerializeSessionAsync(session);

// Phase 2: Simulate an application restart by opening a fresh provider and agent,
// then resuming the same session from its serialized state.
AnsiConsole.MarkupLine("\n[bold]--- Phase 2: Resuming after 'restart' ---[/]");

using var provider2 = new SqliteChatHistoryProvider(DbPath);

AIAgent agent2 = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    ChatOptions = new() { Instructions = Instructions },
    Name = "MemoryAgent",
    ChatHistoryProvider = provider2,
});

AgentSession resumedSession = await agent2.DeserializeSessionAsync(serializedSession);
AnsiConsole.MarkupLine($"[grey]Resumed SQLite key: {provider2.GetSessionDbKey(resumedSession)}[/]");

AnsiConsole.MarkupLine("[blue]User:[/] What do you remember about me?");
response = await agent2.RunAsync("What do you remember about me?", resumedSession);
AnsiConsole.MarkupLine($"[green]Agent:[/] {Markup.Escape(response.Text)}");

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
/// A custom <see cref="ChatHistoryProvider"/> that persists chat messages in a local
/// SQLite database — file-based persistence without an external service like Redis.
/// The per-session key is held in the <see cref="AgentSession"/> state bag, so it
/// round-trips automatically when the session is serialized and resumed.
/// </summary>
sealed class SqliteChatHistoryProvider : ChatHistoryProvider, IDisposable
{
    private readonly ProviderSessionState<string> _sessionState;
    private readonly SqliteConnection _connection;
    private IReadOnlyList<string>? _stateKeys;

    public SqliteChatHistoryProvider(string dbPath, string? stateKey = null)
    {
        _sessionState = new ProviderSessionState<string>(
            _ => Guid.NewGuid().ToString("N"),
            stateKey ?? nameof(SqliteChatHistoryProvider));

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();

        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                message_json TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public override IReadOnlyList<string> StateKeys => _stateKeys ??= [_sessionState.StateKey];

    public string GetSessionDbKey(AgentSession session) =>
        _sessionState.GetOrInitializeState(session);

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        var sessionDbKey = _sessionState.GetOrInitializeState(context.Session);

        var messages = new List<ChatMessage>();
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT message_json FROM messages WHERE session_id = $sid ORDER BY id";
        command.Parameters.AddWithValue("$sid", sessionDbKey);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(JsonSerializer.Deserialize<ChatMessage>(
                reader.GetString(0), AIJsonUtilities.DefaultOptions)!);
        }

        return messages;
    }

    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context, CancellationToken cancellationToken = default)
    {
        var sessionDbKey = _sessionState.GetOrInitializeState(context.Session);
        var newMessages = context.RequestMessages.Concat(context.ResponseMessages ?? []);

        using var transaction = _connection.BeginTransaction();
        foreach (var message in newMessages)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO messages (session_id, message_json) VALUES ($sid, $json)";
            command.Parameters.AddWithValue("$sid", sessionDbKey);
            command.Parameters.AddWithValue(
                "$json", JsonSerializer.Serialize(message, AIJsonUtilities.DefaultOptions));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    public void Dispose() => _connection.Dispose();
}
