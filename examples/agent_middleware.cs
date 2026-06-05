/*
Middleware flow diagram:

 agent.RunAsync("user message")
 │
 ▼
 ┌─────────────────────────────────────────────────┐
 │         Agent Run Middleware                     │
 │  (timing, blocking)                             │
 │                                                 │
 │  ┌───────────────────────────────────────────┐  │
 │  │       Chat Client Middleware              │  │
 │  │  (logging, message counting)              │  │
 │  │                                           │  │
 │  │        ┌──────────────┐                   │  │
 │  │        │   AI Model   │                   │  │
 │  │        └──────┬───────┘                   │  │
 │  │               │ tool calls                │  │
 │  │               ▼                           │  │
 │  │  ┌──────────────────────────────────┐     │  │
 │  │  │     Function Middleware          │     │  │
 │  │  │  (logging, timing)               │     │  │
 │  │  │                                  │     │  │
 │  │  │  get_weather(), get_date(), ...  │     │  │
 │  │  └──────────────────────────────────┘     │  │
 │  │               │                           │  │
 │  │               ▼                           │  │
 │  │        ┌──────────────┐                   │  │
 │  │        │   AI Model   │                   │  │
 │  │        │  (final ans) │                   │  │
 │  │        └──────────────┘                   │  │
 │  └───────────────────────────────────────────┘  │
 └─────────────────────────────────────────────────┘
 │
 ▼
 response
*/

#:sdk Microsoft.NET.Sdk
#:package Microsoft.Agents.AI@1.6.2
#:package Microsoft.Agents.AI.OpenAI@1.6.2
#:package Azure.AI.OpenAI@2.9.0-beta.1
#:package Azure.Identity@1.21.0
#:package OpenAI@2.10.0
#:package DotNetEnv@3.2.0
#:package Spectre.Console@0.55.2
#:property NoWarn=IL2026;IL3050

using System.ClientModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Azure.AI.OpenAI;
using Azure.Identity;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using Spectre.Console;

Env.Load();

string apiHost = Environment.GetEnvironmentVariable("API_HOST") ?? "azure";

IChatClient chatClient = CreateChatClient(apiHost);

// ---- Wrap chat client with chat-level middleware ----

int totalMessageCount = 0;

IChatClient middlewareChatClient = chatClient
    .AsBuilder()
    .Use(getResponseFunc: LoggingChatMiddleware, getStreamingResponseFunc: null)
    .Use(getResponseFunc: MessageCountChatMiddleware, getStreamingResponseFunc: null)
    .Build();

// ---- Build agent with tools ----

var toolJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
};

var tools = new[]
{
    AIFunctionFactory.Create(GetWeather, name: "GetWeather", serializerOptions: toolJsonOptions),
    AIFunctionFactory.Create(GetCurrentDate, name: "GetCurrentDate")
};

AIAgent baseAgent = middlewareChatClient.AsAIAgent(
    instructions: "You help users plan their weekends. Use the available tools to check the weather and date.",
    name: "MiddlewareDemo",
    tools: tools);

// ---- Add agent-level middleware (run + function) ----

AIAgent agent = baseAgent
    .AsBuilder()
    .Use(LoggingFunctionMiddleware)
    .Use(TimingFunctionMiddleware)
    .Use(runFunc: TimingAgentRunMiddleware, runStreamingFunc: null)
    .Use(runFunc: BlockingAgentRunMiddleware, runStreamingFunc: null)
    .Build();

// ---- Run examples ----

// Example 1: Normal request — all middleware fires
AnsiConsole.MarkupLine("[bold]=== Normal Request ===[/]");
var response = await agent.RunAsync("What's the weather like this weekend in San Francisco?");
AnsiConsole.MarkupLine($"[green]{Markup.Escape(response.Text)}[/]");

// Example 2: Blocked request — blocking middleware terminates early
AnsiConsole.MarkupLine("\n[bold]=== Blocked Request ===[/]");
response = await agent.RunAsync("Tell me about nuclear physics.");
AnsiConsole.MarkupLine($"[green]{Markup.Escape(response.Text)}[/]");

// Example 3: Per-request middleware — extra agent run middleware for a single call
AnsiConsole.MarkupLine("\n[bold]=== Request with Per-Request Middleware ===[/]");

var perRequestAgent = agent
    .AsBuilder()
    .Use(runFunc: PerRequestAgentRunMiddleware, runStreamingFunc: null)
    .Build();

response = await perRequestAgent.RunAsync("What's the weather like in Portland?");
AnsiConsole.MarkupLine($"[green]{Markup.Escape(response.Text)}[/]");

// ---- Tools ----

[Description("Returns weather data for a given city.")]
static WeatherReport GetWeather(
    [Description("The city to get the weather for")] string city)
{
    AnsiConsole.MarkupLine($"[grey]  Getting weather for {Markup.Escape(city)}[/]");
    return Random.Shared.NextDouble() < 0.05
        ? new WeatherReport(72, "Sunny")
        : new WeatherReport(60, "Rainy");
}

[Description("Get the current date from the system.")]
static string GetCurrentDate()
{
    AnsiConsole.MarkupLine("[grey]  Getting current date[/]");
    return DateTime.Now.ToString("yyyy-MM-dd");
}

// ---- Agent Run Middleware ----

async Task<AgentResponse> TimingAgentRunMiddleware(
    IEnumerable<ChatMessage> messages,
    AgentSession? session,
    AgentRunOptions? options,
    AIAgent innerAgent,
    CancellationToken cancellationToken)
{
    var sw = Stopwatch.StartNew();
    AnsiConsole.MarkupLine("[blue]⏲️  [[Agent Run]] Starting agent execution[/]");

    var result = await innerAgent.RunAsync(messages, session, options, cancellationToken);

    sw.Stop();
    AnsiConsole.MarkupLine($"[blue]⏲️  [[Agent Run]] Execution completed in {sw.Elapsed.TotalSeconds:F2}s[/]");
    return result;
}

async Task<AgentResponse> BlockingAgentRunMiddleware(
    IEnumerable<ChatMessage> messages,
    AgentSession? session,
    AgentRunOptions? options,
    AIAgent innerAgent,
    CancellationToken cancellationToken)
{
    string[] blockedWords = ["nuclear", "classified"];

    var lastMessage = messages.LastOrDefault();
    if (lastMessage?.Text is { } text)
    {
        foreach (var word in blockedWords)
        {
            if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine($"[red]❌ [[Agent Run]] Request blocked: contains '{Markup.Escape(word)}'[/]");
                return new AgentResponse([new ChatMessage(ChatRole.Assistant, $"Sorry, I can't process requests about '{word}'.")]);
            }
        }
    }

    return await innerAgent.RunAsync(messages, session, options, cancellationToken);
}

async Task<AgentResponse> PerRequestAgentRunMiddleware(
    IEnumerable<ChatMessage> messages,
    AgentSession? session,
    AgentRunOptions? options,
    AIAgent innerAgent,
    CancellationToken cancellationToken)
{
    AnsiConsole.MarkupLine("[magenta]🏃 [[Per-Request]] This middleware only applies to this run[/]");
    var result = await innerAgent.RunAsync(messages, session, options, cancellationToken);
    AnsiConsole.MarkupLine("[magenta]🏃 [[Per-Request]] Run completed[/]");
    return result;
}

// ---- Function Middleware ----

async ValueTask<object?> LoggingFunctionMiddleware(
    AIAgent agent,
    FunctionInvocationContext context,
    Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
    CancellationToken cancellationToken)
{
    AnsiConsole.MarkupLine($"[yellow]🪵 [[Function]] Calling {Markup.Escape(context.Function.Name)} with args: {Markup.Escape(context.Arguments?.ToString() ?? "")}[/]");

    var result = await next(context, cancellationToken);

    AnsiConsole.MarkupLine($"[yellow]🪵 [[Function]] {Markup.Escape(context.Function.Name)} returned: {Markup.Escape(result?.ToString() ?? "null")}[/]");
    return result;
}

async ValueTask<object?> TimingFunctionMiddleware(
    AIAgent agent,
    FunctionInvocationContext context,
    Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
    CancellationToken cancellationToken)
{
    var sw = Stopwatch.StartNew();
    AnsiConsole.MarkupLine($"[cyan]⌚ [[Function]] Starting {Markup.Escape(context.Function.Name)}[/]");

    var result = await next(context, cancellationToken);

    sw.Stop();
    AnsiConsole.MarkupLine($"[cyan]⌚ [[Function]] {Markup.Escape(context.Function.Name)} took {sw.Elapsed.TotalMilliseconds:F1}ms[/]");
    return result;
}

// ---- Chat Client Middleware ----

async Task<ChatResponse> LoggingChatMiddleware(
    IEnumerable<ChatMessage> messages,
    ChatOptions? options,
    IChatClient innerChatClient,
    CancellationToken cancellationToken)
{
    AnsiConsole.MarkupLine($"[purple]💬 [[Chat]] Sending {messages.Count()} messages to AI[/]");

    var response = await innerChatClient.GetResponseAsync(messages, options, cancellationToken);

    AnsiConsole.MarkupLine("[purple]💬 [[Chat]] AI response received[/]");
    return response;
}

async Task<ChatResponse> MessageCountChatMiddleware(
    IEnumerable<ChatMessage> messages,
    ChatOptions? options,
    IChatClient innerChatClient,
    CancellationToken cancellationToken)
{
    int count = messages.Count();
    totalMessageCount += count;
    AnsiConsole.MarkupLine($"[purple]🔢 [[Chat]] Messages in this request: {count}, total so far: {totalMessageCount}[/]");

    var response = await innerChatClient.GetResponseAsync(messages, options, cancellationToken);

    AnsiConsole.MarkupLine("[purple]🔢 [[Chat]] Chat response received[/]");
    return response;
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

record WeatherReport(int Temperature, string Description);
