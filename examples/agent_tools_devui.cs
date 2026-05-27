#:sdk Microsoft.NET.Sdk.Web
#:package Microsoft.Agents.AI@1.6.2
#:package Microsoft.Agents.AI.OpenAI@1.6.2
#:package Microsoft.Agents.AI.DevUI@1.6.2-preview.260521.1
#:package Microsoft.Agents.AI.Hosting@1.6.2-preview.260521.1
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
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using OpenAI;
using Spectre.Console;

// Microsoft.NET.Sdk.Web sets the process CWD to the .cs file's folder, so a plain
// Env.Load() would miss the workspace-root .env. TraversePath() walks upward until
// it finds one.
Env.TraversePath().Load();

string apiHost = Environment.GetEnvironmentVariable("API_HOST") ?? "azure";

JsonSerializerOptions ToolJsonOptions =
    new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddChatClient(CreateChatClient(apiHost));

builder.AddAIAgent(
        "weekend-planner",
        "You help users plan their weekends and choose the best activities for the given weather. " +
        "If an activity would be unpleasant in weather, don't suggest it. " +
        "Include date of the weekend in response.")
    .WithAITools(
        AIFunctionFactory.Create(GetWeather, name: "get_weather", serializerOptions: ToolJsonOptions),
        AIFunctionFactory.Create(GetActivities, name: "get_activities", serializerOptions: ToolJsonOptions),
        AIFunctionFactory.Create(GetCurrentDate, name: "get_current_date", serializerOptions: ToolJsonOptions));

builder.AddDevUI();
builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();

var app = builder.Build();

app.MapOpenAIResponses();
app.MapOpenAIConversations();
app.MapDevUI();

AnsiConsole.MarkupLine("[green]Open the DevUI at[/] [link]<host>/devui[/] [grey](the listen URL is printed by ASP.NET below).[/]");
AnsiConsole.MarkupLine("[grey]OpenAI Responses API is mapped at /v1/responses. Press Ctrl+C to stop.[/]");

app.Run();

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
