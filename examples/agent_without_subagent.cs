// Context bloat without sub-agents.
//
// When an agent uses tools that return large outputs (file contents, search
// results, database rows), all that raw data accumulates in the agent's
// context window. Over multiple tool calls, this bloats the context,
// increasing cost and potentially degrading performance.
//
// This example demonstrates the problem: a single agent reads and searches
// source files directly. Every line of source code flows into the agent's
// context window alongside the conversation.
//
//  agent.RunAsync("user question")
//   │
//   ▼
//  ┌──────────────────────────────────────────────────┐
//  │              Code Research Agent                 │
//  │                                                  │
//  │  1. Calls list_project_files() → full listing    │
//  │  2. Calls read_project_file() → entire file      │
//  │     contents added to context (repeated N times) │
//  │  3. Calls search_project_files() → all matching  │
//  │     lines added to context                       │
//  │  4. Generates answer from bloated context        │
//  └──────────────────────────────────────────────────┘
//   │
//   ▼
//  response (agent saw ALL raw file contents)
//
// Compare with agent_with_subagent.cs to see how sub-agents solve this.

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
using System.Runtime.CompilerServices;
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

const string UserQuery =
    "What different patterns are used across this project? " +
    "Read the relevant files to find out.";

AIAgent codingAssistant = chatClient.AsAIAgent(
    instructions:
        "You are a helpful coding assistant. You answer questions about " +
        "codebases, explain patterns, and help developers understand code. " +
        "Use the available tools to list, read, and search C# source files " +
        "in the project. You MUST read several relevant files before answering " +
        "— do not answer from search snippets alone. Then provide a clear, " +
        "well-organized answer.",
    name: "CodingAssistant",
    tools:
    [
        AIFunctionFactory.Create(
            ListProjectFiles,
            name: "list_project_files",
            serializerOptions: ToolJsonOptions),
        AIFunctionFactory.Create(
            ReadProjectFile,
            name: "read_project_file",
            serializerOptions: ToolJsonOptions),
        AIFunctionFactory.Create(
            SearchProjectFiles,
            name: "search_project_files",
            serializerOptions: ToolJsonOptions),
    ]);

AnsiConsole.Write(new Rule("[bold deepskyblue1]Code Research WITHOUT Sub-Agents[/]").LeftJustified().RuleStyle("deepskyblue1 dim"));
AnsiConsole.MarkupLine("[dim]All file contents flow directly into the agent's context window.[/]");
AnsiConsole.WriteLine();
AnsiConsole.MarkupLine($"[deepskyblue1]User:[/] {Markup.Escape(UserQuery)}");
AnsiConsole.WriteLine();

var response = await codingAssistant.RunAsync(UserQuery);

AnsiConsole.WriteLine();
AnsiConsole.Write(new Rule("[bold green]Assistant[/]").LeftJustified().RuleStyle("green dim"));
AnsiConsole.MarkupLine($"[green]{Markup.Escape(response.Text)}[/]");
AnsiConsole.WriteLine();

var usageTable = new Table()
    .Border(TableBorder.Rounded)
    .Title("[bold]Token Usage[/]")
    .AddColumn("Agent")
    .AddColumn(new TableColumn("Input").RightAligned())
    .AddColumn(new TableColumn("Output").RightAligned())
    .AddColumn(new TableColumn("Total").RightAligned());

usageTable.AddRow(
    "[yellow]Assistant[/]",
    FormatTokens(response.Usage?.InputTokenCount),
    FormatTokens(response.Usage?.OutputTokenCount),
    FormatTokens(response.Usage?.TotalTokenCount));

AnsiConsole.Write(usageTable);
AnsiConsole.WriteLine();
AnsiConsole.MarkupLine("[dim]All raw file contents were in the agent's context window.[/]");
AnsiConsole.MarkupLine("[dim]Compare with agent_with_subagent.cs to see context isolation in action.[/]");

// ----------------------------------------------------------------------------------
// File tools (read-only, sandboxed to the examples directory)
// ----------------------------------------------------------------------------------

[Description("List all files in the given directory under the examples folder.")]
static string ListProjectFiles(
    [Description("Relative directory path within the examples folder, e.g. '.' or 'spanish'.")] string directory)
{
    AnsiConsole.MarkupLine($"[grey46]  └ tool:[/] [wheat4]list_project_files('{Markup.Escape(directory)}')[/]");
    if (!TryResolveUnderProject(directory, out string? target, out string? error))
        return error;
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
    AnsiConsole.MarkupLine($"[grey46]  └ tool:[/] [wheat4]read_project_file('{Markup.Escape(filepath)}')[/]");
    if (!TryResolveUnderProject(filepath, out string? target, out string? error))
        return error;
    if (!File.Exists(target))
        return $"Error: file '{filepath}' not found.";

    return File.ReadAllText(target);
}

[Description("Search all .cs files in the examples folder for lines containing the query string (case-insensitive).")]
static string SearchProjectFiles(
    [Description("Text to search for (case-insensitive) across all .cs files in the examples folder.")] string query)
{
    AnsiConsole.MarkupLine($"[grey46]  └ tool:[/] [wheat4]search_project_files('{Markup.Escape(query)}')[/]");
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

// ----------------------------------------------------------------------------------
// Helpers
// ----------------------------------------------------------------------------------

// Resolves the project directory using the source file's own location at compile
// time. File-based apps put the build output in a temp folder, so AppContext
// .BaseDirectory is the wrong anchor. [CallerFilePath] gives the original path.
static string GetProjectDir([CallerFilePath] string sourceFile = "") =>
    Path.GetFullPath(
        Environment.GetEnvironmentVariable("PROJECT_DIR")
        ?? Path.GetDirectoryName(sourceFile)!);

// Tool arguments are model-generated and untrusted: guard against absolute paths
// (Path.Combine ignores the root if the second arg is rooted) and .. traversal.
static bool TryResolveUnderProject(string relativePath, out string resolved, out string error)
{
    string root = GetProjectDir();
    string full = Path.GetFullPath(Path.Combine(root, relativePath));
    string rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
    if (!string.Equals(full, root, StringComparison.OrdinalIgnoreCase)
        && !full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
    {
        resolved = "";
        error = $"Error: path '{relativePath}' is outside the examples directory.";
        return false;
    }
    resolved = full;
    error = "";
    return true;
}

static string FormatTokens(long? value) =>
    value is null ? "n/a" : value.Value.ToString("N0");

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
