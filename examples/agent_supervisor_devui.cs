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

IChatClient chatClient = CreateChatClient(apiHost);

JsonSerializerOptions ToolJsonOptions =
    new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

// Specialist 1: weekend planning, with its own tools.
AIAgent weekendAgent = chatClient.AsAIAgent(
    instructions:
        "You help users plan their weekends and choose the best activities for the given weather. " +
        "If an activity would be unpleasant in the weather, don't suggest it. " +
        "Include the date of the weekend in your response.",
    name: "WeekendPlanner",
    description: "Plans a weekend of activities based on the user's request and the weather.",
    tools:
    [
        AIFunctionFactory.Create(GetWeather, serializerOptions: ToolJsonOptions),
        AIFunctionFactory.Create(GetActivities, serializerOptions: ToolJsonOptions),
        AIFunctionFactory.Create(GetCurrentDate, serializerOptions: ToolJsonOptions),
    ]);

// Specialist 2: meal planning, with its own tools.
AIAgent mealAgent = chatClient.AsAIAgent(
    instructions:
        "You help users plan meals and choose the best recipes. " +
        "Include the ingredients and cooking instructions in your response. " +
        "Indicate what the user needs to buy from the store when their fridge is missing ingredients.",
    name: "MealPlanner",
    description: "Plans a meal and recipe based on the user's request and what's in the fridge.",
    tools:
    [
        AIFunctionFactory.Create(FindRecipes, serializerOptions: ToolJsonOptions),
        AIFunctionFactory.Create(CheckFridge, serializerOptions: ToolJsonOptions),
    ]);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddChatClient(chatClient);

// Supervisor: orchestrates the two specialists by exposing each as a tool via AsAIFunction().
builder.AddAIAgent(
        "Supervisor",
        "You are a supervisor managing two specialist agents: a weekend planning agent and a meal planning agent. " +
        "Break down the user's request, decide which specialist (or both) to call via the available tools, " +
        "and then synthesize a final helpful answer. When invoking a tool, provide clear, concise queries.")
    .WithAITools(
        weekendAgent.AsAIFunction(),
        mealAgent.AsAIFunction());

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

// ----------------------------------------------------------------------------------
// Weekend planning tools
// ----------------------------------------------------------------------------------

[Description("Returns weather data for a given city and date.")]
static WeatherReport GetWeather(
    [Description("The city to get the weather for.")] string city,
    [Description("The date to get weather for in format YYYY-MM-DD.")] string date)
{
    AnsiConsole.MarkupLine($"[grey]Getting weather for {Markup.Escape(city)} on {date}[/]");
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

// ----------------------------------------------------------------------------------
// Meal planning tools
// ----------------------------------------------------------------------------------

[Description("Returns recipes based on a query for a desired meal or ingredient.")]
static Recipe[] FindRecipes(
    [Description("User query or desired meal/ingredient.")] string query)
{
    AnsiConsole.MarkupLine($"[grey]Finding recipes for '{Markup.Escape(query)}'[/]");
    if (query.Contains("pasta", StringComparison.OrdinalIgnoreCase))
    {
        return
        [
            new Recipe(
                "Pasta Primavera",
                ["pasta", "vegetables", "olive oil"],
                ["Cook pasta.", "Sauté vegetables."]),
        ];
    }
    if (query.Contains("tofu", StringComparison.OrdinalIgnoreCase))
    {
        return
        [
            new Recipe(
                "Tofu Stir Fry",
                ["tofu", "soy sauce", "vegetables"],
                ["Cube tofu.", "Stir fry veggies."]),
        ];
    }
    return
    [
        new Recipe(
            "Grilled Cheese Sandwich",
            ["bread", "cheese", "butter"],
            ["Butter bread.", "Place cheese between slices.", "Grill until golden brown."]),
    ];
}

[Description("Returns a list of ingredients currently in the fridge.")]
static string[] CheckFridge()
{
    AnsiConsole.MarkupLine("[grey]Checking fridge for current ingredients[/]");
    return Random.Shared.NextDouble() < 0.5
        ? ["pasta", "tomato sauce", "bell peppers", "olive oil"]
        : ["tofu", "soy sauce", "broccoli", "carrots"];
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
record Recipe(string Title, string[] Ingredients, string[] Steps);
