<!--
---
name: Dotnet Agent Framework Demos
description: Collection of .NET examples for Microsoft Agent Framework using Microsoft Foundry.
languages:
- csharp
- dotnet
products:
- azure-openai
- azure
- ai-services
page_type: sample
urlFragment: dotnet-agentframework-demos
---
-->
# Dotnet Agent Framework Demos

[![Open in GitHub Codespaces](https://img.shields.io/static/v1?style=for-the-badge&label=GitHub+Codespaces&message=Open&color=brightgreen&logo=github)](https://codespaces.new/Azure-Samples/dotnet-agentframework-demos)
[![Open in Dev Containers](https://img.shields.io/static/v1?style=for-the-badge&label=Dev%20Containers&message=Open&color=blue&logo=visualstudiocode)](https://vscode.dev/redirect?url=vscode://ms-vscode-remote.remote-containers/cloneInVolume?url=https://github.com/Azure-Samples/dotnet-agentframework-demos)

This repository provides examples of [Microsoft Agent Framework](https://learn.microsoft.com/agent-framework/) using LLMs from [Microsoft Foundry](https://learn.microsoft.com/azure/ai-foundry/) or other model providers.

Every example is a single-file .NET 10 app (`*.cs`) that you run directly with `dotnet run`. No solution files, no project files — NuGet dependencies are declared inline at the top of each file using `#:package` directives.

* [Getting started](#getting-started)
  * [GitHub Codespaces](#github-codespaces)
  * [VS Code Dev Containers](#vs-code-dev-containers)
  * [Local environment](#local-environment)
* [Configuring model providers](#configuring-model-providers)
  * [Using Microsoft Foundry models](#using-microsoft-foundry-models)
    * [Authentication](#authentication)
  * [Using OpenAI.com models](#using-openaicom-models)
  * [Using local Ollama models](#using-local-ollama-models)
* [Running the .NET examples](#running-the-net-examples)
* [Resources](#resources)

## Getting started

You have a few options for getting started with this repository.
The quickest way to get started is GitHub Codespaces, since it will setup everything for you, but you can also [set it up locally](#local-environment).

### GitHub Codespaces

You can run this repository virtually by using GitHub Codespaces. The button will open a web-based VS Code instance in your browser:

1. Open the repository (this may take several minutes):

    [![Open in GitHub Codespaces](https://github.com/codespaces/badge.svg)](https://codespaces.new/Azure-Samples/dotnet-agentframework-demos)

2. Open a terminal window
3. Continue with the steps to run the examples

### VS Code Dev Containers

A related option is VS Code Dev Containers, which will open the project in your local VS Code using the [Dev Containers extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers):

1. Start Docker Desktop (install it if not already installed)
2. Open the project:

    [![Open in Dev Containers](https://img.shields.io/static/v1?style=for-the-badge&label=Dev%20Containers&message=Open&color=blue&logo=visualstudiocode)](https://vscode.dev/redirect?url=vscode://ms-vscode-remote.remote-containers/cloneInVolume?url=https://github.com/Azure-Samples/dotnet-agentframework-demos)

3. In the VS Code window that opens, once the project files show up (this may take several minutes), open a terminal window.
4. Continue with the steps to run the examples

The dev container includes a Redis server, which is used by the `agent_history_redis.cs` example.

### Local environment

1. Make sure the following tools are installed:

    * [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
    * Git

2. Clone the repository:

    ```shell
    git clone https://github.com/Azure-Samples/dotnet-agentframework-demos
    cd dotnet-agentframework-demos
    ```

3. (No restore step needed — file-based apps fetch NuGet packages the first time you run them.)

4. *Optional:* To run the `agent_history_redis.cs` example, you need a Redis server running locally:

    ```shell
    docker run -d -p 6379:6379 redis:7-alpine
    ```

5. *Optional:* To run the PostgreSQL examples (`agent_knowledge_postgres.cs`, `agent_knowledge_pg.cs`, `agent_knowledge_pg_rewrite.cs`), you need PostgreSQL with pgvector running locally:

    ```shell
    docker run -d -p 5432:5432 -e POSTGRES_USER=admin -e POSTGRES_PASSWORD=LocalPasswordOnly pgvector/pgvector:pg17
    ```

## Configuring model providers

These examples can be run with Microsoft Foundry or OpenAI.com, depending on the environment variables you set. All the examples reference the environment variables from a `.env` file, and an example `.env.sample` file is provided. Host-specific instructions are below.

## Using Microsoft Foundry models

This project includes infrastructure as code (IaC) to provision Azure OpenAI deployments of "gpt-5.4" and "text-embedding-3-large" via Microsoft Foundry. The IaC is defined in the `infra` directory and uses the Azure Developer CLI to provision the resources.

1. Make sure the [Azure Developer CLI (azd)](https://aka.ms/install-azd) is installed.

2. Login to Azure:

    ```shell
    azd auth login
    ```

    For GitHub Codespaces users, if the previous command fails, try:

   ```shell
    azd auth login --use-device-code
    ```

    If you are using a tenant besides the default tenant, you may need to also login with Azure CLI to that tenant:

    ```shell
    az login --tenant your-tenant-id
    ```

3. Provision the OpenAI account:

    ```shell
    azd provision
    ```

    It will prompt you to provide an `azd` environment name (like "agents-demos"), select a subscription from your Azure account, and select a location. Then it will provision the resources in your account.

4. Once the resources are provisioned, you should now see a local `.env` file with all the environment variables needed to run the examples.
5. To delete the resources, run:

    ```shell
    azd down
    ```

### Authentication

The examples authenticate to Azure OpenAI using `AzureCliCredential` because they are intended to be run as local demos — they pick up the identity from your `az login` (or `azd auth login`) session without any extra setup. For production workloads, switch to `ManagedIdentityCredential` instead so the app authenticates with its assigned managed identity rather than a developer's signed-in CLI session.

## Using OpenAI.com models

1. Create a `.env` file by copying the `.env.sample` file and updating it with your OpenAI API key and desired model name.

    ```bash
    cp .env.sample .env
    ```

2. Update the `.env` file with your OpenAI API key and desired model name:

    ```bash
    API_HOST=openai
    OPENAI_API_KEY=your_openai_api_key
    OPENAI_MODEL=gpt-4o-mini
    ```

## Using local Ollama models

Most examples can also run against local Ollama models through Ollama's OpenAI-compatible endpoint.
First install [Ollama](https://ollama.com/), start it, and pull chat and embedding models:

```shell
ollama pull qwen3.5:4b
ollama pull nomic-embed-text
```

Then configure `.env`:

```bash
API_HOST=ollama
OLLAMA_ENDPOINT=http://localhost:11434/v1
OLLAMA_API_KEY=nokeyneeded
OLLAMA_MODEL=qwen3.5:4b
OLLAMA_EMBEDDING_MODEL=nomic-embed-text
EMBEDDING_DIMENSIONS=256
```

Use `http://localhost:11434/v1` when Ollama and the .NET process run on the same machine. If the examples run in a
dev container while Ollama runs on the host, use `http://host.docker.internal:11434/v1` instead.

## Running the .NET examples

Run any example from the repository root with `dotnet run`:

```shell
dotnet run examples/agent_basic.cs
```

The first run will restore NuGet packages declared at the top of the file (this can take a moment). Subsequent runs are fast.

Each example demonstrates a different Agent Framework pattern.

| Example | Description |
| ------- | ----------- |
| [agent_basic.cs](examples/agent_basic.cs) | A basic informational agent. |
| [agent_tool.cs](examples/agent_tool.cs) | An agent with a single weather tool. |
| [agent_tools.cs](examples/agent_tools.cs) | A weekend planning agent with multiple tools. |
| [agent_session.cs](examples/agent_session.cs) | In-memory sessions for multi-turn conversations with memory across messages. |
| [agent_history_sqlite.cs](examples/agent_history_sqlite.cs) | Persistent chat history with a custom SQLite history provider for local file-based conversation persistence. |
| [agent_history_redis.cs](examples/agent_history_redis.cs) | Persistent chat history with Redis for conversation history that survives restarts. |
| [agent_memory_redis.cs](examples/agent_memory_redis.cs) | Long-term memory with RedisContextProvider, storing and retrieving conversational context from Redis. |
| [agent_memory_mem0.cs](examples/agent_memory_mem0.cs) | Long-term memory with Mem0 OSS, extracting and recalling distilled user facts across sessions. |
| [agent_supervisor.cs](examples/agent_supervisor.cs) | A supervisor orchestrating activity and recipe sub-agents. |
| [agent_with_subagent.cs](examples/agent_with_subagent.cs) | Context isolation with sub-agents to keep prompts focused on relevant tools. |
| [agent_without_subagent.cs](examples/agent_without_subagent.cs) | Context bloat example where one agent carries all tool schemas in a single prompt. |
| [agent_summarization.cs](examples/agent_summarization.cs) | Context compaction via summarization middleware to reduce token usage in long conversations. |
| [workflow_magenticone.cs](examples/workflow_magenticone.cs) | A MagenticOne multi-agent workflow. |
| [agent_tool_approval.cs](examples/agent_tool_approval.cs) | Standalone agent with tool approval — gates sensitive operations before execution. |
| [agent_middleware.cs](examples/agent_middleware.cs) | Agent, chat, and function middleware for logging, timing, and blocking. |
| [agent_knowledge_aisearch.cs](examples/agent_knowledge_aisearch.cs) | Knowledge retrieval (RAG) using Azure AI Search with AgentFrameworkAzureAISearchRAG. |
| [agent_knowledge_sqlite.cs](examples/agent_knowledge_sqlite.cs) | Knowledge retrieval (RAG) using a custom context provider with SQLite FTS5. |
| [agent_knowledge_pg.cs](examples/agent_knowledge_pg.cs) | Knowledge retrieval (RAG) with PostgreSQL hybrid search (pgvector + full-text) using Reciprocal Rank Fusion. |
| [agent_knowledge_pg_rewrite.cs](examples/agent_knowledge_pg_rewrite.cs) | Knowledge retrieval with query rewriting for multi-turn conversations over PostgreSQL. |
| [agent_knowledge_postgres.cs](examples/agent_knowledge_postgres.cs) | Knowledge retrieval (RAG) with PostgreSQL hybrid search (pgvector + full-text) using Reciprocal Rank Fusion. |
| [agent_mcp_remote.cs](examples/agent_mcp_remote.cs) | An agent using a remote MCP server (Microsoft Learn) for documentation search. |
| [agent_mcp_local.cs](examples/agent_mcp_local.cs) | An agent connected to a local MCP server (e.g. for expense logging). |
| [openai_tool_calling.cs](examples/openai_tool_calling.cs) | Tool calling with the low-level OpenAI SDK, showing manual tool dispatch. |
| [workflow_rag_ingest.cs](examples/workflow_rag_ingest.cs) | A RAG ingestion pipeline using plain executors: fetch a document, split into chunks, and embed with an OpenAI model. |
| [workflow_fan_out_fan_in_edges.cs](examples/workflow_fan_out_fan_in_edges.cs) | Fan-out/fan-in with explicit edge groups using `AddFanOutEdges` and `AddFanInEdges`. |
| [workflow_aggregator_summary.cs](examples/workflow_aggregator_summary.cs) | Fan-out/fan-in with LLM summarization: synthesize expert outputs into an executive brief. |
| [workflow_aggregator_structured.cs](examples/workflow_aggregator_structured.cs) | Fan-out/fan-in with LLM structured extraction into a typed record (`ResponseFormat`). |
| [workflow_aggregator_voting.cs](examples/workflow_aggregator_voting.cs) | Fan-out/fan-in with majority-vote aggregation across multiple classifiers (pure logic tally). |
| [workflow_aggregator_ranked.cs](examples/workflow_aggregator_ranked.cs) | Fan-out/fan-in with LLM-as-judge ranking: score and rank multiple candidates into a typed list. |
| [workflow_agents.cs](examples/workflow_agents.cs) | A workflow with AI agents as executors: a Writer drafts content and a Reviewer provides feedback. |
| [workflow_agents_sequential.cs](examples/workflow_agents_sequential.cs) | A sequential orchestration using `SequentialBuilder`: Writer and Reviewer run in order while sharing full conversation history. |
| [workflow_agents_streaming.cs](examples/workflow_agents_streaming.cs) | The same Writer → Reviewer workflow using streaming runs to observe `executor_invoked`, `executor_completed`, and streaming `output` events in real-time. |
| [workflow_agents_concurrent.cs](examples/workflow_agents_concurrent.cs) | Concurrent orchestration using `ConcurrentBuilder`: run specialist agents in parallel and collect merged conversations. |
| [workflow_conditional.cs](examples/workflow_conditional.cs) | A minimal workflow with conditional edges: the Reviewer routes to a Publisher (approved) or Editor (needs revision) based on a sentinel token. |
| [workflow_conditional_structured.cs](examples/workflow_conditional_structured.cs) | The same conditional-edge routing pattern, but with structured reviewer output for typed branch decisions instead of sentinel string matching. |
| [workflow_conditional_state.cs](examples/workflow_conditional_state.cs) | A stateful conditional workflow with iterative revision loops: stores the latest draft in workflow state and publishes from that state after approval. |
| [workflow_conditional_state_isolated.cs](examples/workflow_conditional_state_isolated.cs) | The stateful conditional workflow using a `CreateWorkflow(...)` factory to build fresh agents/workflow per task for state isolation and thread safety. |
| [workflow_switch_case.cs](examples/workflow_switch_case.cs) | A workflow with switch-case routing: a Classifier agent uses structured outputs to categorize a message and route to a specialized handler. |
| [workflow_multi_selection_edge_group.cs](examples/workflow_multi_selection_edge_group.cs) | LLM-powered multi-selection routing using `AddMultiSelectionEdgeGroup` to activate one-or-many downstream handlers. |
| [workflow_converge.cs](examples/workflow_converge.cs) | A branch-and-converge workflow: Reviewer routes to Publisher or Editor, then converges before final summary output. |
| [workflow_handoffbuilder.cs](examples/workflow_handoffbuilder.cs) | Autonomous handoff orchestration using `HandoffBuilder` (agents transfer control without human-in-the-loop). |
| [workflow_handoffbuilder_rules.cs](examples/workflow_handoffbuilder_rules.cs) | Handoff orchestration with explicit routing rules using `HandoffBuilder.AddHandoff()`. |
| [workflow_hitl_requests.cs](examples/workflow_hitl_requests.cs) | Simple HITL chat — always pause for human input after every agent response. |
| [workflow_hitl_requests_structured.cs](examples/workflow_hitl_requests_structured.cs) | Trip planner HITL with structured outputs — agent decides when to ask vs. finish via `PlannerOutput.Status`. |
| [workflow_hitl_tool_approval.cs](examples/workflow_hitl_tool_approval.cs) | Email agent workflow with always-require approval mode for gating sensitive tool calls. |
| [workflow_hitl_checkpoint.cs](examples/workflow_hitl_checkpoint.cs) | Content review with `FileCheckpointStorage` — pause, exit process, and resume from checkpoint. |
| [workflow_hitl_checkpoint_pg.cs](examples/workflow_hitl_checkpoint_pg.cs) | Same content review workflow with a custom `PostgresCheckpointStorage` backend. |
| [workflow_hitl_handoff.cs](examples/workflow_hitl_handoff.cs) | Interactive handoff (no autonomous mode) — framework pauses for user input via `HandoffAgentUserRequest`. |
| [agent_otel_aspire.cs](examples/agent_otel_aspire.cs) | An agent with OpenTelemetry tracing, metrics, and structured logs exported to the [Aspire Dashboard](https://aspire.dev/dashboard/standalone/). |
| [agent_otel_appinsights.cs](examples/agent_otel_appinsights.cs) | An agent with OpenTelemetry tracing, metrics, and structured logs exported to [Azure Application Insights](https://learn.microsoft.com/azure/azure-monitor/app/app-insights-overview). Requires Azure provisioning via `azd provision`. |
| [agent_evaluation_generate.cs](examples/agent_evaluation_generate.cs) | Generate synthetic evaluation data for the travel planner agent. |
| [agent_evaluation.cs](examples/agent_evaluation.cs) | Evaluate a travel planner agent using [Azure AI Evaluation](https://learn.microsoft.com/azure/ai-foundry/concepts/evaluation-evaluators/agent-evaluators) agent evaluators (IntentResolution, ToolCallAccuracy, TaskAdherence, ResponseCompleteness). Optionally set `AZURE_AI_PROJECT` in `.env` to log results to [Microsoft Foundry](https://learn.microsoft.com/azure/ai-foundry/how-to/develop/agent-evaluate-sdk). |
| [agent_evaluation_batch.cs](examples/agent_evaluation_batch.cs) | Batch evaluation of agent responses using Azure AI Evaluation's `Evaluate()` function. |
| [agent_redteam.cs](examples/agent_redteam.cs) | Red-team a financial advisor agent using [Azure AI Evaluation](https://learn.microsoft.com/azure/ai-foundry/how-to/develop/red-teaming-agent) to test resilience against adversarial attacks across risk categories (Violence, HateUnfairness, Sexual, SelfHarm). Requires `AZURE_AI_PROJECT` in `.env`. |

## Using the Aspire Dashboard for telemetry

The [agent_otel_aspire.cs](examples/agent_otel_aspire.cs) example can export OpenTelemetry traces, metrics, and structured logs to a [Aspire Dashboard](https://aspire.dev/dashboard/standalone/).

### In GitHub Codespaces / Dev Containers

The Aspire Dashboard runs automatically as a service alongside the dev container. No extra setup is needed.

1. The `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable is already set by the dev container.

2. Run the example:

    ```sh
    dotnet run examples/agent_otel_aspire.cs
    ```

3. Open the dashboard at <http://localhost:18888> and explore:

    * **Traces**: See the full span tree — agent invocation → chat completion → tool execution
    * **Metrics**: View token usage and operation duration histograms
    * **Structured Logs**: Browse conversation messages (system, user, assistant, tool)
    * **GenAI visualizer**: Select a chat completion span to see the rendered conversation

### Local environment (without Dev Containers)

If you're running locally without Dev Containers, you need to start the Aspire Dashboard manually:

1. Start the Aspire Dashboard:

    ```sh
    docker run --rm -it -d -p 18888:18888 -p 4317:18889 --name aspire-dashboard \
        -e DASHBOARD__FRONTEND__AUTHMODE=Unsecured \
        mcr.microsoft.com/dotnet/aspire-dashboard:latest
    ```

2. Add the OTLP endpoint to your `.env` file:

    ```sh
    OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
    ```

3. Run the example:

    ```sh
    dotnet run examples/agent_otel_aspire.cs
    ```

4. Open the dashboard at <http://localhost:18888> and explore.

5. When done, stop the dashboard:

    ```shell
    docker stop aspire-dashboard
    ```

For the full .NET + Aspire guide, see [Use the Aspire dashboard with .NET apps](https://aspire.dev/dashboard/standalone-for-dotnet/).

## Exporting telemetry to Azure Application Insights

The [agent_otel_appinsights.cs](examples/agent_otel_appinsights.cs) example exports OpenTelemetry traces, metrics, and structured logs to [Azure Application Insights](https://learn.microsoft.com/azure/azure-monitor/app/app-insights-overview).

### Setup

This example requires an `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable. You can get this automatically or manually:

**Option A: Automatic via `azd provision`**

If you run `azd provision` (see [Using Microsoft Foundry models](#using-microsoft-foundry-models)), the Application Insights resource is provisioned automatically and the connection string is written to your `.env` file.

**Option B: Manual from the Azure Portal**

1. Create an Application Insights resource in the [Azure Portal](https://portal.azure.com).
2. Copy the connection string from the resource's Overview page.
3. Add it to your `.env` file:

    ```sh
    APPLICATIONINSIGHTS_CONNECTION_STRING=InstrumentationKey=...;IngestionEndpoint=...
    ```

### Running the example

```sh
dotnet run examples/agent_otel_appinsights.cs
```

### Viewing telemetry

After running the example, navigate to your Application Insights resource in the Azure Portal:

* **Transaction search**: See end-to-end traces for agent invocations, chat completions, and tool executions.
* **Live Metrics**: Monitor real-time request rates and performance.
* **Performance**: Analyze operation durations and identify bottlenecks.

Telemetry data may take 2–5 minutes to appear in the portal.

## Resources

* [Agent Framework Documentation](https://learn.microsoft.com/agent-framework/)
* [Microsoft.Agents.AI on NuGet](https://www.nuget.org/packages/Microsoft.Agents.AI)
* [.NET 10 file-based apps](https://learn.microsoft.com/dotnet/core/tools/dotnet-run-file)
