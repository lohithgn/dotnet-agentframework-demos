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
- **Redis**: `#:package StackExchange.Redis@<latest>` (no first-party `Microsoft.Agents.AI.Redis` exists on NuGet yet)
- **Azure AI Search**: SDK route is `#:package Azure.Search.Documents@<latest>` (no first-party `Microsoft.Agents.AI.AzureAISearch` exists on NuGet yet)

`<latest>` placeholders are filled in when the corresponding example is ported.

## Canonical client-selection block

This block matches the Python repo's `API_HOST` switch. Place it right after `Env.Load()`. The result is an `IChatClient` you hand to `new ChatClientAgent(...)`.

```csharp
string apiHost = Environment.GetEnvironmentVariable("API_HOST") ?? "azure";

IChatClient chatClient = apiHost switch
{
    "azure" => new AzureOpenAIClient(
            new Uri(Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")!),
            new DefaultAzureCredential())
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
```

For examples that use embeddings, the equivalent embedding-client block selects between `AzureOpenAIClient.GetEmbeddingClient(...)`, `OpenAIClient.GetEmbeddingClient(...)`, and the Ollama variant — using `AZURE_OPENAI_EMBEDDING_DEPLOYMENT`, `OPENAI_EMBEDDING_MODEL`, and `OLLAMA_EMBEDDING_MODEL` respectively.

## Conventions

- **Snake_case filenames** for examples (`agent_basic.cs`, `workflow_handoffbuilder_rules.cs`) — matches the Python repo so descriptions and the README table line up 1:1.
- **PascalCase** for C# types, methods, and properties. Local variables and parameters are camelCase.
- **`Env.Load()`** is the first executable line. The block above goes immediately after.
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
