/// Context compaction via summarization.
///
/// When a conversation grows long, the accumulated messages can exceed the
/// model's context window or become expensive. The CompactionProvider monitors
/// token usage and, once a threshold is crossed, asks the LLM to summarize the
/// conversation so far. The summary replaces the old messages, freeing up
/// context space for future turns.
///
/// Diagram:
///
///  agent.RunAsync("user message")
///  │
///  ▼
///  ┌──────────────────────────────────────────────────┐
///  │   CompactionProvider (AIContextProvider)          │
///  │                                                  │
///  │  1. Check cumulative token usage                 │
///  │  2. If over threshold → summarize old messages   │
///  │     via LLM and replace them with summary        │
///  │  3. Agent processes turn normally                 │
///  │  4. Token usage tracked automatically            │
///  └──────────────────────────────────────────────────┘
///  │
///  ▼
///  response

#:sdk Microsoft.NET.Sdk
#:package Microsoft.Agents.AI@1.6.2
#:package Microsoft.Agents.AI.OpenAI@1.6.2
#:package Azure.AI.OpenAI@2.9.0-beta.1
#:package Azure.Identity@1.21.0
#:package OpenAI@2.10.0
#:package DotNetEnv@3.2.0
#:package Spectre.Console@0.55.2
#:property NoWarn=IL2026;IL3050;MAAI001

using System.ClientModel;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Azure.AI.OpenAI;
using Azure.Identity;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using OpenAI;
using Spectre.Console;

Env.Load();

string apiHost = Environment.GetEnvironmentVariable("API_HOST") ?? "azure";

IChatClient chatClient = CreateChatClient(apiHost);

// ── Tools ────────────────────────────────────────────────────────────

var weatherTool = AIFunctionFactory.Create(GetWeather);
var activitiesTool = AIFunctionFactory.Create(GetActivities);

// ── Compaction setup ─────────────────────────────────────────────────

// Use a low token threshold for demo purposes so summarization triggers quickly.
// In production, set this to something like 4000–8000 depending on model context window.
var compaction = new SummarizationCompactionStrategy(
    chatClient,
    CompactionTriggers.TokensExceed(500));

// ── Agent setup ──────────────────────────────────────────────────────

AIAgent agent = chatClient.AsAIAgent(new ChatClientAgentOptions
{
    Name = "WeekendPlanner",
    ChatOptions = new()
    {
        Instructions =
            "You are a helpful weekend-planning assistant. Help users plan "
            + "their weekends by checking weather and suggesting activities. "
            + "Be friendly and provide detailed recommendations.",
        Tools = [weatherTool, activitiesTool]
    },
    AIContextProviders = [new CompactionProvider(compaction)]
});

// ── Multi-turn conversation ──────────────────────────────────────────

AnsiConsole.MarkupLine("\n[bold]=== Context Compaction with Summarization ===[/]");
AnsiConsole.MarkupLine("[dim]Token threshold: 500[/]");
AnsiConsole.MarkupLine("[dim]The compaction provider will summarize the conversation once token usage exceeds the threshold.[/]\n");

AgentSession session = await agent.CreateSessionAsync();

string[] prompts =
[
    "What's the weather like in San Francisco this weekend?",
    "How about Portland? What's the weather and what activities can I do there?",
    "What about Seattle? Give me the full picture — weather and things to do.",
    "Of all the cities we discussed, which one has the best combination of weather and activities?",
    "Great, let's go with that city. What should I pack?",
];

foreach (string prompt in prompts)
{
    AnsiConsole.MarkupLine($"[blue]User:[/] {Markup.Escape(prompt)}");

    var response = await agent.RunAsync(prompt, session);
    AnsiConsole.MarkupLine($"[green]Agent:[/] {Markup.Escape(response.Text)}\n");

    // Show message count to visualize compaction happening
    if (session.TryGetInMemoryChatHistory(out var history))
    {
        AnsiConsole.MarkupLine($"[dim]  [[Messages in history: {history.Count}]][/]");
    }

    if (response.Usage is { } usage)
    {
        AnsiConsole.MarkupLine($"[dim]  [[Token usage — input: {usage.InputTokenCount}, output: {usage.OutputTokenCount}, total: {usage.TotalTokenCount}]][/]\n");
    }
}

// ── Tool functions ───────────────────────────────────────────────────

[Description("Returns weather data for a given city.")]
static string GetWeather(
    [Description("The city to get the weather for.")] string city)
{
    AnsiConsole.MarkupLine($"[grey]  ↳ Getting weather for {Markup.Escape(city)}[/]");
    string[] conditions = ["sunny", "cloudy", "rainy", "snowy"];
    int temp = Random.Shared.Next(30, 91);
    return $"The weather in {city} is {conditions[Random.Shared.Next(conditions.Length)]} with a high of {temp}°F.";
}

[Description("Returns popular weekend activities for a given city.")]
static string GetActivities(
    [Description("The city to find activities in.")] string city)
{
    AnsiConsole.MarkupLine($"[grey]  ↳ Finding activities in {Markup.Escape(city)}[/]");
    string[] allActivities =
    [
        "Visit the farmer's market",
        "Hike at the local state park",
        "Check out a food truck festival",
        "Go to the art museum",
        "Take a walking tour of downtown",
        "Visit the botanical garden",
        "Catch a live music show",
        "Try a new brunch spot",
    ];
    var picked = allActivities.OrderBy(_ => Random.Shared.Next()).Take(3);
    return $"Popular activities in {city}: {string.Join(", ", picked)}.";
}

// ── Client factory ───────────────────────────────────────────────────

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
