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

JsonSerializerOptions ToolJsonOptions =
    new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

AIAgent agent = chatClient.AsAIAgent(
    instructions:
        "You help users plan their weekends and choose the best activities for the given weather. " +
        "If an activity would be unpleasant in weather, don't suggest it. " +
        "Include date of the weekend in response.",
    name: "weekend-planner",
    tools:
    [
        AIFunctionFactory.Create(GetWeather, serializerOptions: ToolJsonOptions),
        AIFunctionFactory.Create(GetActivities, serializerOptions: ToolJsonOptions),
        AIFunctionFactory.Create(GetCurrentDate, serializerOptions: ToolJsonOptions),
    ]);

var response = await agent.RunAsync("what can I do this weekend in San Francisco?");
AnsiConsole.MarkupLine($"[green]{Markup.Escape(response.Text)}[/]");

[Description("Returns weather data for a given city.")]
static WeatherReport GetWeather(
    [Description("The city to get the weather for.")] string city)
{
    AnsiConsole.MarkupLine($"[grey]Getting weather for {Markup.Escape(city)}[/]");
    return Random.Shared.NextDouble() < 0.05
        ? new WeatherReport(72, "Sunny")
        : new WeatherReport(60, "Rainy");
}

[Description("Returns a list of activities for a given city and date.")]
static Activity[] GetActivities(
    [Description("The city to get activities for.")] string city,
    [Description("The date to get activities for in format YYYY-MM-DD.")] string date)
{
    AnsiConsole.MarkupLine($"[grey]Getting activities for {Markup.Escape(city)} on {date}[/]");
    return
    [
        new Activity("Hiking", city),
        new Activity("Beach", city),
        new Activity("Museum", city),
    ];
}

[Description("Gets the current date from the system and returns as a string in format YYYY-MM-DD.")]
static string GetCurrentDate()
{
    AnsiConsole.MarkupLine("[grey]Getting current date[/]");
    return DateTime.Now.ToString("yyyy-MM-dd");
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
record Activity(string Name, string Location);
