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

var weatherTool = AIFunctionFactory.Create(
    GetWeather,
    serializerOptions: new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    });

AIAgent agent = chatClient.AsAIAgent(
    instructions: "You are a helpful weather agent.",
    name: "WeatherAgent",
    tools: [weatherTool]);

await ExampleWithoutSessionAsync(agent);
await ExampleWithSessionAsync(agent);
await ExampleSessionAcrossAgentsAsync(chatClient, agent, weatherTool);

static async Task ExampleWithoutSessionAsync(AIAgent agent)
{
    AnsiConsole.MarkupLine("\n[bold]=== Without Session (No Memory) ===[/]");

    AnsiConsole.MarkupLine("[blue]User:[/] What's the weather like in Seattle?");
    var response = await agent.RunAsync("What's the weather like in Seattle?");
    AnsiConsole.MarkupLine($"[green]Agent:[/] {Markup.Escape(response.Text)}");

    AnsiConsole.MarkupLine("\n[blue]User:[/] What was the last city I asked about?");
    response = await agent.RunAsync("What was the last city I asked about?");
    AnsiConsole.MarkupLine($"[green]Agent:[/] {Markup.Escape(response.Text)}");
}

static async Task ExampleWithSessionAsync(AIAgent agent)
{
    AnsiConsole.MarkupLine("\n[bold]=== With Session (Persistent Memory) ===[/]");

    AgentSession session = await agent.CreateSessionAsync();

    AnsiConsole.MarkupLine("[blue]User:[/] What's the weather like in Tokyo?");
    var response = await agent.RunAsync("What's the weather like in Tokyo?", session);
    AnsiConsole.MarkupLine($"[green]Agent:[/] {Markup.Escape(response.Text)}");

    AnsiConsole.MarkupLine("\n[blue]User:[/] How about London?");
    response = await agent.RunAsync("How about London?", session);
    AnsiConsole.MarkupLine($"[green]Agent:[/] {Markup.Escape(response.Text)}");

    AnsiConsole.MarkupLine("\n[blue]User:[/] Which of those cities has better weather?");
    response = await agent.RunAsync("Which of those cities has better weather?", session);
    AnsiConsole.MarkupLine($"[green]Agent:[/] {Markup.Escape(response.Text)}");
}

static async Task ExampleSessionAcrossAgentsAsync(IChatClient chatClient, AIAgent agent, AIFunction weatherTool)
{
    AnsiConsole.MarkupLine("\n[bold]=== Session Across Agent Instances ===[/]");

    AgentSession session = await agent.CreateSessionAsync();

    AnsiConsole.MarkupLine("[blue]User:[/] What's the weather in Paris?");
    var response = await agent.RunAsync("What's the weather in Paris?", session);
    AnsiConsole.MarkupLine($"[green]Agent 1:[/] {Markup.Escape(response.Text)}");

    // Create a second agent and continue with the same session
    AIAgent agent2 = chatClient.AsAIAgent(
        instructions: "You are a helpful weather agent.",
        name: "WeatherAgent2",
        tools: [weatherTool]);

    AnsiConsole.MarkupLine("\n[blue]User:[/] What was the last city I asked about?");
    response = await agent2.RunAsync("What was the last city I asked about?", session);
    AnsiConsole.MarkupLine($"[green]Agent 2:[/] {Markup.Escape(response.Text)}");
}

[Description("Returns weather data for a given city.")]
static WeatherReport GetWeather(
    [Description("The city to get the weather for.")] string city)
{
    AnsiConsole.MarkupLine($"[grey]Getting weather for {Markup.Escape(city)}[/]");
    string[] conditions = ["sunny", "cloudy", "rainy", "stormy"];
    return new WeatherReport(
        conditions[Random.Shared.Next(conditions.Length)],
        Random.Shared.Next(10, 31));
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

record WeatherReport(string Condition, int HighC);
