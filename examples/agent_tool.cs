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
        }
);

AIAgent agent = chatClient.AsAIAgent(
    instructions: "You're an informational agent. Answer questions cheerfully.",
    name: "InfoAgent",
    tools: [weatherTool]);

var response = await agent.RunAsync("how's weather today in Singapore?");
AnsiConsole.MarkupLine($"[green]{Markup.Escape(response.Text)}[/]");

[Description("Returns weather data for a given city.")]
static WeatherReport GetWeather(
    [Description("City name, spelled out fully")] string city)
{
    AnsiConsole.MarkupLine($"[grey]Getting weather for {Markup.Escape(city)}[/]");
    return Random.Shared.NextDouble() < 0.05
        ? new WeatherReport(72, "Sunny")
        : new WeatherReport(60, "Rainy");
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
