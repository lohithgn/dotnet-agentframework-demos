// Context isolation with sub-agents.
//
// When an agent delegates tool-heavy work to a sub-agent, the sub-agent's
// context window absorbs all the raw tool output (file contents, search
// results, etc.). The main agent only sees the sub-agent's concise summary,
// keeping its own context window small and focused.
//
// This is the "context quarantine" pattern described in:
// - LangChain deep agents: https://docs.langchain.com/oss/python/deepagents/subagents
// - Manus context engineering: https://rlancemartin.github.io/2025/10/15/manus/
// - Google ADK architecture: https://cloud.google.com/blog/topics/developers-practitioners/where-to-use-sub-agents-versus-agents-as-tools/
// - VS Code subagents: https://code.visualstudio.com/docs/copilot/agents/subagents
//
//  agent.RunAsync("user question")
//   │
//   ▼
//  ┌─────────────────────────────────────────────────────────┐
//  │              Coordinator                                │
//  │  (small context — only sees summaries)                  │
//  │                                                         │
//  │  Calls research_codebase("question")                    │
//  │       │                                                 │
//  │       ▼                                                 │
//  │  ┌──────────────────────────────────────────────────┐   │
//  │  │         Research Sub-Agent                       │   │
//  │  │  (isolated context — absorbs all raw content)    │   │
//  │  │                                                  │   │
//  │  │  1. list_project_files() → file listing          │   │
//  │  │  2. read_project_file() → full file contents     │   │
//  │  │  3. search_project_files() → matching lines      │   │
//  │  │  4. Returns concise summary (< 200 words)        │   │
//  │  └──────────────────────────────────────────────────┘   │
//  │       │                                                 │
//  │       ▼ summary text only                               │
//  │  Synthesizes final answer from summary                  │
//  └─────────────────────────────────────────────────────────┘
//   │
//   ▼
//  response (coordinator never saw raw file contents)
//
// Compare with agent_without_subagent.cs to see the difference.

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

// Research sub-agent: owns the file tools. Its context window absorbs the
// verbose tool output. It is required to return a concise (< 200 word)
// summary with filenames — the coordinator never sees raw file contents.
AIAgent researchAgent = chatClient.AsAIAgent(
    instructions:
        "You are a code research assistant. Use the available tools to list, " +
        "read, and search C# source files in the project to answer the " +
        "question. You MUST read several relevant files before summarizing " +
        "— do not summarize from search snippets alone. Be thorough in your " +
        "research but return a CONCISE summary of your findings in UNDER 200 " +
        "WORDS. Cite the filenames you read. Do NOT include raw file " +
        "contents in your response — summarize the key patterns, classes, " +
        "and functions you found.",
    name: "ResearchAgent",
    description: "Reads and searches project files and returns a < 200 word summary.",
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

// We accumulate sub-agent usage here so the report at the end can compare
// coordinator tokens to sub-agent tokens for the same query.
List<UsageDetails> subAgentUsageLog = new();

// Delegation tool. A named local function (not a lambda) closes over
// `researchAgent` and `subAgentUsageLog`. We deliberately do NOT use
// `researchAgent.AsAIFunction()` because that helper hides per-call usage,
// which is the very thing this example exists to compare.
[Description("Delegate a code research question to the research sub-agent. The sub-agent reads/searches files in its own isolated context and returns a concise summary. The coordinator never sees raw file contents.")]
async Task<string> ResearchCodebaseAsync(
    [Description("A research question about the codebase to investigate.")] string question)
{
    AnsiConsole.MarkupLine($"[grey46]  └ Coordinator → ResearchAgent:[/] [wheat4]{Markup.Escape(question)}[/]");
    var subResp = await researchAgent.RunAsync(question);
    if (subResp.Usage is not null)
        subAgentUsageLog.Add(subResp.Usage);
    return string.IsNullOrWhiteSpace(subResp.Text) ? "No findings." : subResp.Text;
}

AIFunction researchCodebaseTool = AIFunctionFactory.Create(
    ResearchCodebaseAsync,
    name: "research_codebase",
    serializerOptions: ToolJsonOptions);

// Coordinator: only has the delegation tool. Its context window stays
// small and focused — it never sees raw file contents.
AIAgent coordinator = chatClient.AsAIAgent(
    instructions:
        "You are a helpful coding assistant. You answer questions about " +
        "codebases, explain patterns, and help developers understand code. " +
        "Use the research_codebase tool to investigate the codebase before " +
        "answering — it will read and search files for you. Provide a " +
        "clear, well-organized answer based on the research results.",
    name: "Coordinator",
    tools: [researchCodebaseTool]);

AnsiConsole.Write(new Rule("[bold deepskyblue1]Code Research WITH Sub-Agents (Context Isolation)[/]").LeftJustified().RuleStyle("deepskyblue1 dim"));
AnsiConsole.MarkupLine("[dim]The coordinator delegates file reading to a research sub-agent.[/]");
AnsiConsole.MarkupLine("[dim]Raw file contents stay in the sub-agent's context, not the coordinator's.[/]");
AnsiConsole.WriteLine();
AnsiConsole.MarkupLine($"[deepskyblue1]User:[/] {Markup.Escape(UserQuery)}");
AnsiConsole.WriteLine();

var response = await coordinator.RunAsync(UserQuery);

AnsiConsole.WriteLine();
AnsiConsole.Write(new Rule("[bold green]Coordinator[/]").LeftJustified().RuleStyle("green dim"));
AnsiConsole.MarkupLine($"[green]{Markup.Escape(response.Text)}[/]");
AnsiConsole.WriteLine();

long? SumNullable(IEnumerable<long?> values)
{
    long total = 0;
    bool anyNonNull = false;
    foreach (var v in values)
    {
        if (v is null) continue;
        total += v.Value;
        anyNonNull = true;
    }
    return anyNonNull ? total : null;
}

var usageTable = new Table()
    .Border(TableBorder.Rounded)
    .Title("[bold]Token Usage[/]")
    .AddColumn("Agent")
    .AddColumn(new TableColumn("Input").RightAligned())
    .AddColumn(new TableColumn("Output").RightAligned())
    .AddColumn(new TableColumn("Total").RightAligned());

usageTable.AddRow(
    "[yellow]Coordinator[/]",
    FormatTokens(response.Usage?.InputTokenCount),
    FormatTokens(response.Usage?.OutputTokenCount),
    FormatTokens(response.Usage?.TotalTokenCount));

usageTable.AddRow(
    "[yellow]Sub-agent (sum)[/]",
    FormatTokens(SumNullable(subAgentUsageLog.Select(u => u.InputTokenCount))),
    FormatTokens(SumNullable(subAgentUsageLog.Select(u => u.OutputTokenCount))),
    FormatTokens(SumNullable(subAgentUsageLog.Select(u => u.TotalTokenCount))));

AnsiConsole.Write(usageTable);
AnsiConsole.WriteLine();
AnsiConsole.MarkupLine("[dim]The coordinator's input tokens are much lower because it never saw[/]");
AnsiConsole.MarkupLine("[dim]raw file contents — only the sub-agent's concise summary.[/]");
AnsiConsole.MarkupLine("[dim]Compare with agent_without_subagent.cs where ALL file contents are in context.[/]");

// ----------------------------------------------------------------------------------
// File tools (read-only, sandboxed to the examples directory) — given to the
// research sub-agent only. The coordinator never sees these directly.
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
