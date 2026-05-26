# Instructions for coding agents

This repository contains many examples of using the Microsoft Agent Framework (MAF) in .NET — the C# edition of `agent-framework`. The Python counterpart lives at https://github.com/Azure-Samples/python-agentframework-demos.

The agent-framework GitHub repo is here:
https://github.com/microsoft/agent-framework
It contains both Python and .NET agent framework code, but we are only using the .NET packages in this repo.

MAF is changing rapidly still, so we sometimes need to check the repo changelog and issues to see if there are any breaking changes that might affect our code.
The .NET changelog is here:
https://github.com/microsoft/agent-framework/blob/main/dotnet/CHANGELOG.md

MAF documentation is available on Microsoft Learn here:
https://learn.microsoft.com/agent-framework/
When available, the MS Learn MCP server can be used to explore the documentation, ask questions, and get code examples.

## Project shape — .NET 10 file-based apps

Every example is a single `.cs` file under `examples/` that runs directly with:

```bash
dotnet run examples/agent_basic.cs
```

There is **no** `.sln`, **no** `.csproj`, **no** `Directory.Build.props`, and **no** shared library project. NuGet dependencies are declared inline at the top of each `.cs` file using `#:sdk` and `#:package` directives — same model as Python's "one runnable script per example", where every file is self-contained.

The .NET SDK version is pinned in `global.json` (currently `10.0.100`, `rollForward: latestFeature`).

## Canonical example header

Every example starts with the same preamble. Copy this block verbatim at the top of any new example, then add or remove `#:package` lines based on what the example actually uses (Redis, Postgres, OTel, etc.).

```csharp
#:sdk Microsoft.NET.Sdk
#:package Microsoft.Agents.AI@1.6.2
#:package Microsoft.Agents.AI.OpenAI@1.6.2
#:package Azure.AI.OpenAI@2.9.0-beta.1
#:package Azure.Identity@1.21.0
#:package OpenAI@2.10.0
#:package DotNetEnv@3.2.0
#:package Spectre.Console@0.55.2

using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using DotNetEnv;
using Microsoft.Agents.AI;
using OpenAI;
using Spectre.Console;

Env.Load();
```

> Versions verified against NuGet on 2026-05-26. Latest stable for `Microsoft.Agents.AI*` (1.6.2), `OpenAI` (2.10.0), `Azure.Identity` (1.21.0), `DotNetEnv` (3.2.0), and `Spectre.Console` (0.55.2). `Azure.AI.OpenAI` uses the latest 2.9.0-beta.1 (no newer stable than 2.1.0 exists yet) to track current Azure OpenAI surface. Workflow examples additionally add `#:package Microsoft.Agents.AI.Workflows@1.6.2`. The same versions are duplicated verbatim across every file — do not introduce a "shared versions" file.

### Extras by feature

Examples that need additional packages:

- **Workflows**: `#:package Microsoft.Agents.AI.Workflows@1.6.2`
- **Mem0 memory**: `#:package Microsoft.Agents.AI.Mem0@1.0.0-preview.251028.1` (only preview available)
- **MCP client/server**: `#:package ModelContextProtocol@<latest>` (verify before use)
- **OTel / Aspire**: `#:package OpenTelemetry.Exporter.OpenTelemetryProtocol@<latest>`
- **App Insights**: `#:package Azure.Monitor.OpenTelemetry.Exporter@<latest>`
- **Postgres + pgvector**: `#:package Npgsql@<latest>`, `#:package Pgvector.Npgsql@<latest>`
- **Redis**: `#:package StackExchange.Redis@<latest>` — see "Redis pattern" below.
- **Azure AI Search**: `#:package Azure.Search.Documents@<latest>` — see "Azure AI Search pattern" below.

`<latest>` placeholders are filled in when the corresponding example is ported.

### Redis pattern (no first-party MAF Redis package)

Python ships `agent_framework.redis.RedisHistoryProvider`. There is **no equivalent** `Microsoft.Agents.AI.Redis` package on NuGet — the only first-party history/memory providers in .NET MAF today are `InMemoryChatHistoryProvider` (in `Microsoft.Agents.AI.Abstractions`), `ChatHistoryMemoryProvider` (in `Microsoft.Agents.AI`, vector-store-backed), and `CosmosChatHistoryProvider` (in `Microsoft.Agents.AI.CosmosNoSql`).

For Redis examples, port the Python sample by writing a custom `ChatHistoryProvider` subclass — the same pattern the repo demonstrates in [`dotnet/samples/02-agents/Agents/Agent_Step04_3rdPartyChatHistoryStorage/Program.cs`](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/02-agents/Agents/Agent_Step04_3rdPartyChatHistoryStorage/Program.cs). The sample's `VectorChatHistoryProvider` is the canonical template: override `ProvideChatHistoryAsync` and `StoreChatHistoryAsync`, hold a `ProviderSessionState<State>` to round-trip a session DB key through `AgentSession.StateBag`, and back it with `StackExchange.Redis` (`IDatabase.HashSet` / `SortedSetAdd` for ordered messages, or `RediSearch` for vector memory).

For `agent_memory_redis.cs`, prefer `ChatHistoryMemoryProvider` over a `VectorStore` whose backing connector is Redis — but verify the connector exists on NuGet before porting (`Microsoft.SemanticKernel.Connectors.Redis` is the most likely option). If no usable Redis vector connector is published, fall back to a custom provider using RediSearch directly.

### Azure AI Search pattern (no first-party MAF AzureAISearch context provider)

Python ships `agent_framework.azure.AzureAISearchContextProvider` with `mode="semantic"` and `mode="agentic"`. There is **no equivalent** `Microsoft.Agents.AI.AzureAISearch` context provider on NuGet.

The canonical .NET pattern is `TextSearchProvider` (from `Microsoft.Agents.AI`) wired to a `SearchClient`-backed adapter. Reference implementation: [`dotnet/samples/04-hosting/FoundryHostedAgents/responses/Hosted-AzureSearchRag/Program.cs`](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/04-hosting/FoundryHostedAgents/responses/Hosted-AzureSearchRag/Program.cs). The adapter signature is:

```csharp
static Func<string, CancellationToken, Task<IEnumerable<TextSearchProvider.TextSearchResult>>>
    CreateSearchAdapter(SearchClient client, int top = 3) =>
    async (query, ct) =>
    {
        var response = await client.SearchAsync<SearchDocument>(query, new SearchOptions { Size = top }, ct);
        var results = new List<TextSearchProvider.TextSearchResult>();
        await foreach (var hit in response.Value.GetResultsAsync().WithCancellation(ct))
        {
            results.Add(new TextSearchProvider.TextSearchResult { /* SourceName, SourceLink, Value */ });
        }
        return results;
    };
```

It then plugs into the agent via `ChatClientAgentOptions.AIContextProviders = [new TextSearchProvider(adapter, options)]`. This covers the **semantic mode** half of the Python provider. There is no out-of-the-box .NET helper for **agentic mode** (KnowledgeBase orchestration) — if the Python example exercises it, document the gap in the ported `.cs` file rather than reimplementing Knowledge Base call planning by hand.

Required extras for these examples: `#:package Azure.Search.Documents@<latest>` plus the standard MAF header.

## Canonical client-selection block

This block matches the Python repo's `API_HOST` switch. Read `apiHost` at the top of the file right after `Env.Load()`, hand it to `CreateChatClient`, and put the `static` local function at the **bottom** of the file. The result is an `IChatClient` you turn into an agent via `chatClient.AsAIAgent(instructions: ..., name: ...)` (the prescribed pattern from the [Azure OpenAI Agents docs](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-openai?pivots=programming-language-csharp)).

Copy this block **byte-identical** into every example — same whitespace, same identifier names, same `static` modifier. Identical blocks are bulk-updatable with a single find/replace when MAF ships a breaking change. Do **not** factor this into a shared file.

```csharp
Env.Load();

string apiHost = Environment.GetEnvironmentVariable("API_HOST") ?? "azure";

IChatClient chatClient = CreateChatClient(apiHost);

// ... example-specific code (agent creation, run, etc.) ...

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
```

Why this exact shape:

- **`AzureCliCredential`** (not `DefaultAzureCredential`) because every example is run locally as a demo against your `az login` session. The README documents how to swap to `ManagedIdentityCredential` for production.
- **`AsAIAgent` extension** (not `new ChatClientAgent(...)`) because that is what the Azure OpenAI Agents docs prescribe.
- **`apiHost` read at the top, passed as a parameter** so the dependency is visible at the call site and the local function can be `static` (no accidental closure capture).
- **`static` local function** so it can't accidentally capture outer variables and so future contributors don't grow it into a closure over example state.

For examples that use embeddings, add a parallel `static IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingClient(string apiHost)` local function next to `CreateChatClient` — selecting between `AzureOpenAIClient.GetEmbeddingClient(...)`, `OpenAIClient.GetEmbeddingClient(...)`, and the Ollama variant using `AZURE_OPENAI_EMBEDDING_DEPLOYMENT`, `OPENAI_EMBEDDING_MODEL`, and `OLLAMA_EMBEDDING_MODEL`. Yes, this duplicates the `AzureOpenAIClient` / `OpenAIClient` construction — that's deliberate. Do not factor out a shared `CreateAzureClient` helper.

## Conventions

- **Snake_case filenames** for examples (`agent_basic.cs`, `workflow_handoffbuilder_rules.cs`) — matches the Python repo so descriptions and the README table line up 1:1.
- **PascalCase** for C# types, methods, and properties. Local variables and parameters are camelCase.
- **`Env.Load()`** is the first executable line. The canonical block above goes immediately after.
- **No top-level `try/catch`** unless the example is specifically demonstrating error handling. Let exceptions propagate so the stack trace is visible.
- **`Spectre.Console`** for any colored / styled output (`AnsiConsole.MarkupLine`, `AnsiConsole.Prompt`). It maps cleanly to the Python repo's `rich` usage.
- **`DefaultAzureCredential`** is the only auth path for Azure resources. No connection strings, no keys.
- **Comments and identifiers stay in English.** This repo has no Spanish counterpart.

## Package management

This is a file-based-apps repo — there is no `dotnet restore`. Packages are resolved on first `dotnet run`. To upgrade a package:

1. Find the package on https://www.nuget.org/.
2. Update the `#:package Foo@x.y.z` line in every example that references it.
3. Run the example to trigger restore.

When adding a new package to one example, scan the rest of the repo with `grep_search` to see if other examples should also adopt it.

## Function tools

The canonical shape (from the [Azure OpenAI Agents docs](https://learn.microsoft.com/en-us/agent-framework/agents/providers/azure-openai?pivots=programming-language-csharp#function-tools)) is a `static` method annotated with `[Description]` on both the method and each parameter, wrapped with `AIFunctionFactory.Create(...)` and passed to `AsAIAgent(..., tools: [...])`.

```csharp
AIAgent agent = chatClient.AsAIAgent(
    instructions: "...",
    name: "InfoAgent",
    tools: [AIFunctionFactory.Create(GetWeather, serializerOptions: ToolJsonOptions)]);

[Description("Returns weather data for a given city.")]
static WeatherReport GetWeather(
    [Description("City name, spelled out fully")] string city)
{
    AnsiConsole.MarkupLine($"[grey]Getting weather for {Markup.Escape(city)}[/]");
    return new WeatherReport(60, "Rainy");
}

static readonly JsonSerializerOptions ToolJsonOptions =
    new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

record WeatherReport(int Temperature, string Description);
```

Notes that bite when porting Python tool examples:

- **Always pass `serializerOptions:` with an explicit `TypeInfoResolver = new DefaultJsonTypeInfoResolver()`** when the tool accepts or returns a custom type (`record`, POCO, `Dictionary<,>` of POCOs, etc.). The default `AIFunctionFactory` uses a source-generated `JsonSerializerContext` that only knows BCL primitives, so anything else fails at first call with `JsonTypeInfo metadata for type 'Foo' was not provided`. Plain `new JsonSerializerOptions(JsonSerializerDefaults.Web)` is **not** enough — it throws `JsonSerializerOptions instance must specify a TypeInfoResolver setting before being marked as read-only`.
- **`DefaultJsonTypeInfoResolver` triggers `IL2026` and `IL3050`** AOT-trimming warnings. File-based examples aren't AOT-compiled, so suppress them at the top of the file with `#:property NoWarn=IL2026;IL3050`.
- **Use the snake_case Python parameter description verbatim** where reasonable (e.g. `"City name, spelled out fully"`). It makes the cross-repo diff obvious.
- **Tool logging stays in Spectre.Console** (`AnsiConsole.MarkupLine($"[grey]...[/]")`) rather than `Microsoft.Extensions.Logging`. Matches the Python repo's `rich` log style and avoids pulling in a logger config block.
- **Record/POCO types declared at the bottom of the file**, below the `static` local functions. Top-level statements must precede type declarations.
- For tools that return primitives (`string`, `int`, `bool`) the `serializerOptions:` argument can be omitted — the source-gen context handles those.

`agent_tool.cs` is the reference implementation.

## Debugging Azure .NET SDK HTTP requests

When debugging HTTP interactions between Azure .NET SDKs (like `Azure.AI.OpenAI`, `Azure.AI.Evaluation`) and Azure services, you have a few levers — they are the .NET equivalents of the Python `azure.core` policies.

### 1. Azure SDK logging via `AzureEventSourceListener` (request URLs, headers, status codes)

```csharp
using Azure.Core.Diagnostics;

using var listener = AzureEventSourceListener.CreateConsoleLogger(
    System.Diagnostics.Tracing.EventLevel.Verbose);
```

This emits every Azure SDK pipeline event to the console (request method/URL, response status, retries). Response bodies are typically redacted unless you opt in.

### 2. Opt in to request/response content

Most Azure SDK client option types expose a `Diagnostics` property:

```csharp
var options = new AzureOpenAIClientOptions
{
    Diagnostics =
    {
        IsLoggingContentEnabled = true,
        LoggedContentSizeLimit = 64 * 1024,
    }
};
```

Combine this with the `AzureEventSourceListener` above to see request and response bodies in the console.

### 3. `HttpClient` wire logging

For SDKs that expose `Transport`, you can plug in `Azure.Core.Pipeline.HttpClientTransport` with a custom `HttpClient` whose `HttpClientHandler.InnerHandler` is a logging delegate. Use this only when the EventSource path isn't enough — it's the closest equivalent to Python's `http.client.HTTPConnection.debuglevel = 1`.

## Manual test plan

After upgrading dependencies or making changes across examples, use this plan to verify everything works. Run each example with `dotnet run examples/<file>.cs`.

### No extra setup (Azure OpenAI only)

These work with just `API_HOST=azure` and the standard `.env` from `azd up`:

| Examples | Notes |
|----------|-------|
| `agent_basic.cs` | Interactive chat loop |
| `agent_tool.cs`, `agent_tools.cs` | Tool calling |
| `agent_session.cs` | Session persistence |
| `agent_with_subagent.cs`, `agent_without_subagent.cs` | Sub-agent patterns |
| `agent_supervisor.cs` | Supervisor pattern |
| `agent_middleware.cs` | Middleware pipeline |
| `agent_summarization.cs` | Summarization middleware |
| `agent_tool_approval.cs` | Tool approval |
| `workflow_agents.cs`, `workflow_agents_sequential.cs`, `workflow_agents_concurrent.cs`, `workflow_agents_streaming.cs` | Basic workflows |
| `workflow_conditional.cs`, `workflow_conditional_state.cs`, `workflow_conditional_state_isolated.cs`, `workflow_conditional_structured.cs` | Conditional workflows |
| `workflow_switch_case.cs` | Switch/case workflow |
| `workflow_converge.cs`, `workflow_fan_out_fan_in_edges.cs` | Converge / fan-out patterns |
| `workflow_aggregator_ranked.cs`, `workflow_aggregator_structured.cs`, `workflow_aggregator_summary.cs`, `workflow_aggregator_voting.cs` | Aggregator workflows |
| `workflow_multi_selection_edge_group.cs` | Multi-selection edges |
| `workflow_handoffbuilder.cs`, `workflow_handoffbuilder_rules.cs` | Handoff builder |
| `workflow_hitl_handoff.cs`, `workflow_hitl_requests.cs`, `workflow_hitl_requests_structured.cs`, `workflow_hitl_tool_approval.cs` | HITL workflows |
| `workflow_hitl_checkpoint.cs` | HITL with file-based checkpoints |
| `agent_knowledge_sqlite.cs` | SQLite knowledge provider |
| `agent_history_sqlite.cs` | SQLite history provider |
| `agent_memory_mem0.cs` | Mem0 memory provider |

### Requires Redis (dev container)

Redis runs automatically in the dev container at `redis://redis:6379`.

| Examples | Notes |
|----------|-------|
| `agent_history_redis.cs` | Redis history provider |
| `agent_memory_redis.cs` | Redis memory provider |

### Requires PostgreSQL (dev container)

PostgreSQL runs automatically in the dev container at `postgresql://admin:LocalPasswordOnly@db:5432/postgres`.

| Examples | Notes |
|----------|-------|
| `agent_knowledge_pg.cs` | PG + pgvector knowledge |
| `agent_knowledge_pg_rewrite.cs` | PG knowledge with query rewrite |
| `agent_knowledge_postgres.cs` | PG knowledge (alternative) |
| `workflow_hitl_checkpoint_pg.cs` | HITL with PG-backed checkpoints |

### Requires Azure AI Search

Needs `AZURE_SEARCH_ENDPOINT` and `AZURE_SEARCH_KNOWLEDGE_BASE_NAME` in `.env`.

| Examples | Notes |
|----------|-------|
| `agent_knowledge_aisearch.cs` | Azure AI Search knowledge base (agentic mode) |

### Requires MCP server

Start the MCP server first: `dotnet run examples/mcp_server.cs`

| Examples | Notes |
|----------|-------|
| `agent_mcp_local.cs` | Local MCP server (stdio) |
| `agent_mcp_remote.cs` | Remote MCP server (SSE) |

### Requires OTel / Aspire

| Examples | Notes |
|----------|-------|
| `agent_otel_aspire.cs` | Aspire dashboard (runs in dev container at `http://aspire-dashboard:18888`) |
| `agent_otel_appinsights.cs` | Needs `APPLICATIONINSIGHTS_CONNECTION_STRING` in `.env` |

### Slow-running examples (⏱ 2–10 minutes)

These take significantly longer than other examples:

| Examples | Notes |
|----------|-------|
| `agent_evaluation.cs` | Runs agent + evaluators inline. ~2–3 min. |
| `agent_evaluation_generate.cs` | Generates eval data JSONL. ~2 min. |
| `agent_evaluation_batch.cs` | Batch evaluators on JSONL. ~3–5 min. Needs `eval_data.jsonl` from `agent_evaluation_generate.cs`. |
| `agent_redteam.cs` | Red team attack simulation. ~5–10 min. |
| `workflow_magenticone.cs` | Multi-agent MagenticOne orchestration. ~2–5 min. |
