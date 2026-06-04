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

// Context isolation with sub-agents: the coordinator delegates file research
// to a sub-agent whose context absorbs the raw file contents. The coordinator
// only ever sees the sub-agent's concise summary, so its own context stays
// small. Compare with agent_without_subagent.cs to see the difference.

Env.Load();

string apiHost = Environment.GetEnvironmentVariable("API_HOST") ?? "azure";

IChatClient chatClient = CreateChatClient(apiHost);

const string UserQuery =
    "What different patterns are used across this project? " +
    "Read the relevant files to find out.";

// Research sub-agent: owns the file tools. Its context absorbs the verbose
// tool output. It returns a concise summary instead of raw file contents.
AIAgent researchAgent = chatClient.AsAIAgent(
    instructions:
        "You are a code research assistant. Use the available tools to list, " +
        "read, and search C# source files in the project to answer the " +
        "question. Be thorough in your research but return a CONCISE summary " +
        "of your findings in under 200 words. Do NOT include raw file " +
        "contents in your response — summarize the key patterns, classes, " +
        "and functions you found.",
    name: "ResearchAgent",
    tools:
    [
        AIFunctionFactory.Create(ListProjectFiles),
        AIFunctionFactory.Create(ReadProjectFile),
        AIFunctionFactory.Create(SearchProjectFiles),
    ]);

// Accumulate sub-agent usage so we can compare it to the coordinator's at the end.
List<UsageDetails> subAgentUsageLog = [];

// Delegation tool. We deliberately do NOT use researchAgent.AsAIFunction()
// because that helper hides per-call usage — and the side-by-side token
// comparison is the whole point of this example. A named local function
// (rather than a lambda) closes over researchAgent and subAgentUsageLog
// while staying easy to read.
[Description("Delegate a code research question to the research sub-agent. The sub-agent reads and searches files in its own isolated context, then returns a concise summary. The coordinator never sees the raw file contents.")]
async Task<string> ResearchCodebase(
    [Description("A research question about the codebase to investigate.")] string question)
{
    AnsiConsole.MarkupLine($"[grey]  └ Coordinator → ResearchAgent: {Markup.Escape(question)}[/]");
    var subResp = await researchAgent.RunAsync(question);
    if (subResp.Usage is not null)
        subAgentUsageLog.Add(subResp.Usage);
    return string.IsNullOrWhiteSpace(subResp.Text) ? "No findings." : subResp.Text;
}

// Coordinator: only has the delegation tool. Its context stays small —
// it never sees raw file contents, only the sub-agent's summary.
AIAgent coordinator = chatClient.AsAIAgent(
    instructions:
        "You are a helpful coding assistant. You answer questions about " +
        "codebases, explain patterns, and help developers understand code. " +
        "Use the ResearchCodebase tool to investigate the codebase before " +
        "answering — it will read and search files for you. Provide a " +
        "clear, well-organized answer based on the research results.",
    name: "Coordinator",
    tools: [AIFunctionFactory.Create(ResearchCodebase)]);

AnsiConsole.MarkupLine("\n[bold]=== Code Research WITH Sub-Agents (Context Isolation) ===[/]");
AnsiConsole.MarkupLine("[dim]The coordinator delegates file reading to a research sub-agent.[/]");
AnsiConsole.MarkupLine("[dim]Raw file contents stay in the sub-agent's context, not the coordinator's.[/]\n");

AnsiConsole.MarkupLine($"[blue]User:[/] {Markup.Escape(UserQuery)}");
var response = await coordinator.RunAsync(UserQuery);
AnsiConsole.MarkupLine($"[green]Coordinator:[/] {Markup.Escape(response.Text)}\n");

long coordIn = response.Usage?.InputTokenCount ?? 0;
long coordOut = response.Usage?.OutputTokenCount ?? 0;
long coordTotal = response.Usage?.TotalTokenCount ?? 0;

long subIn = subAgentUsageLog.Sum(u => u.InputTokenCount ?? 0);
long subOut = subAgentUsageLog.Sum(u => u.OutputTokenCount ?? 0);
long subTotal = subAgentUsageLog.Sum(u => u.TotalTokenCount ?? 0);

AnsiConsole.MarkupLine("[bold]── Token Usage ──[/]");
AnsiConsole.MarkupLine($"[yellow]  Coordinator tokens:[/]  input={coordIn:N0}  output={coordOut:N0}  total={coordTotal:N0}");
AnsiConsole.MarkupLine($"[yellow]  Sub-agent tokens:[/]   input={subIn:N0}  output={subOut:N0}  total={subTotal:N0}\n");
AnsiConsole.MarkupLine("[dim]The coordinator's input tokens are much lower because it never saw[/]");
AnsiConsole.MarkupLine("[dim]raw file contents — only the sub-agent's concise summary.[/]");
AnsiConsole.MarkupLine("[dim]Compare with agent_without_subagent.cs where ALL file contents are in context.[/]");

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
