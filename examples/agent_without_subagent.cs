#:sdk Microsoft.NET.Sdk
#:package Microsoft.Agents.AI@1.6.2
#:package Microsoft.Agents.AI.OpenAI@1.6.2
#:package Azure.AI.OpenAI@2.9.0-beta.1
#:package Azure.Identity@1.21.0
#:package OpenAI@2.10.0
#:package DotNetEnv@3.2.0
#:package Spectre.Console@0.55.2

using System.ClientModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Azure.AI.OpenAI;
using Azure.Identity;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using Spectre.Console;

// Context bloat without sub-agents: a single agent reads and searches source
// files directly, so every line of raw content flows into its context window.
// Compare with agent_with_subagent.cs to see how delegating to a sub-agent
// keeps the coordinator's context small.

Env.Load();

string apiHost = Environment.GetEnvironmentVariable("API_HOST") ?? "azure";

IChatClient chatClient = CreateChatClient(apiHost);

const string UserQuery =
    "What different patterns are used across this project? " +
    "Read the relevant files to find out.";

AIAgent codingAssistant = chatClient.AsAIAgent(
    instructions:
        "You are a helpful coding assistant. You answer questions about " +
        "codebases, explain patterns, and help developers understand code. " +
        "Use the available tools to list, read, and search C# source files " +
        "in the project, then provide a clear, well-organized answer.",
    name: "CodingAssistant",
    tools:
    [
        AIFunctionFactory.Create(ListProjectFiles),
        AIFunctionFactory.Create(ReadProjectFile),
        AIFunctionFactory.Create(SearchProjectFiles),
    ]);

AnsiConsole.MarkupLine("\n[bold]=== Code Research WITHOUT Sub-Agents ===[/]");
AnsiConsole.MarkupLine("[dim]All file contents flow directly into the agent's context window.[/]\n");

AnsiConsole.MarkupLine($"[blue]User:[/] {Markup.Escape(UserQuery)}");
var response = await codingAssistant.RunAsync(UserQuery);
AnsiConsole.MarkupLine($"[green]Assistant:[/] {Markup.Escape(response.Text)}\n");

long input = response.Usage?.InputTokenCount ?? 0;
long output = response.Usage?.OutputTokenCount ?? 0;
long total = response.Usage?.TotalTokenCount ?? 0;

AnsiConsole.MarkupLine("[bold]── Token Usage ──[/]");
AnsiConsole.MarkupLine($"[yellow]  Assistant tokens:[/]  input={input:N0}  output={output:N0}  total={total:N0}\n");
AnsiConsole.MarkupLine("[dim]All raw file contents were in the agent's context window.[/]");
AnsiConsole.MarkupLine("[dim]Compare with agent_with_subagent.cs to see context isolation in action.[/]");

[Description("List all files in the given directory under the examples folder.")]
static string ListProjectFiles(
    [Description("Relative directory path within the examples folder, e.g. '.' or 'spanish'.")] string directory)
{
    AnsiConsole.MarkupLine($"[grey]  └ list_project_files('{Markup.Escape(directory)}')[/]");
    string target = Path.Combine(GetProjectDir(), directory);
    if (!Directory.Exists(target))
        return $"Error: directory '{directory}' not found.";

    var entries = Directory.EnumerateFileSystemEntries(target)
        .Select(Path.GetFileName)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
    return string.Join('\n', entries);
}

[Description("Read and return the full contents of a file in the examples folder.")]
static string ReadProjectFile(
    [Description("Relative file path within the examples folder, e.g. 'agent_basic.cs'.")] string filepath)
{
    AnsiConsole.MarkupLine($"[grey]  └ read_project_file('{Markup.Escape(filepath)}')[/]");
    string target = Path.Combine(GetProjectDir(), filepath);
    if (!File.Exists(target))
        return $"Error: file '{filepath}' not found.";

    return File.ReadAllText(target);
}

[Description("Search all .cs files in the examples folder for lines containing the query string (case-insensitive).")]
static string SearchProjectFiles(
    [Description("Text to search for (case-insensitive) across all .cs files in the examples folder.")] string query)
{
    AnsiConsole.MarkupLine($"[grey]  └ search_project_files('{Markup.Escape(query)}')[/]");
    string root = GetProjectDir();
    var results = new List<string>();
    foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.TopDirectoryOnly).OrderBy(p => p))
    {
        string relpath = Path.GetRelativePath(root, path);
        int lineno = 0;
        foreach (string line in File.ReadLines(path))
        {
            lineno++;
            if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
                results.Add($"{relpath}:{lineno}: {line.TrimEnd()}");
        }
    }
    if (results.Count == 0)
        return $"No matches found for '{query}'.";
    if (results.Count > 50)
        return string.Join('\n', results.Take(50)) + $"\n... ({results.Count - 50} more matches truncated)";
    return string.Join('\n', results);
}

// Equivalent of Python's os.path.dirname(__file__). [CallerFilePath] resolves
// at compile time to this source file's path, which is what we want — file-based
// apps put the build output in a temp folder, so AppContext.BaseDirectory and
// the runtime CWD are both unreliable anchors.
static string GetProjectDir([CallerFilePath] string sourceFile = "") =>
    Path.GetDirectoryName(sourceFile)!;

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
