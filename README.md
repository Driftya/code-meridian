<p align="center">
  <img src="docs/assets/icon_512.png" alt="CodeMeridian logo" width="160">
</p>

# CodeMeridian

[![NuGet](https://img.shields.io/nuget/v/CodeMeridian.Indexer.svg)](https://www.nuget.org/packages/CodeMeridian.Indexer)
[![NuGet Downloads](https://img.shields.io/nuget/dt/CodeMeridian.Indexer.svg)](https://www.nuget.org/packages/CodeMeridian.Indexer)
[![CI](https://github.com/Driftya/code-meridian/actions/workflows/ci.yml/badge.svg)](https://github.com/Driftya/code-meridian/actions/workflows/ci.yml)
[![CodeQL](https://github.com/Driftya/code-meridian/actions/workflows/codeql.yml/badge.svg?branch=main)](https://github.com/Driftya/code-meridian/actions/workflows/codeql.yml)
[![Publish Indexer Tool](https://github.com/Driftya/code-meridian/actions/workflows/publish-indexer.yml/badge.svg)](https://github.com/Driftya/code-meridian/actions/workflows/publish-indexer.yml)
[![Publish MCP Image](https://github.com/Driftya/code-meridian/actions/workflows/publish-mcp.yml/badge.svg)](https://github.com/Driftya/code-meridian/actions/workflows/publish-mcp.yml)
[![Dependabot Updates](https://github.com/Driftya/code-meridian/actions/workflows/dependabot/dependabot-updates/badge.svg)](https://github.com/Driftya/code-meridian/actions/workflows/dependabot/dependabot-updates)
[![MCP image](https://img.shields.io/github/v/tag/Driftya/code-meridian?label=MCP%20Image&sort=semver)](https://github.com/Driftya/code-meridian/pkgs/container/codemeridian-mcp)

CodeMeridian is a local graph memory layer for AI coding agents.

It indexes your codebase into Neo4j and exposes that structure through MCP, so AI coding tools can ask precise questions before editing instead of guessing from open files. It works with MCP-compatible clients such as GitHub Copilot, Claude Code, Continue.dev, Codex-style agents, Cline, and local agent workflows.

It is built to be the deterministic context layer for large codebases: callers, dependencies, tests, documentation, hotspots, dead code, diagnostics, and cross-project relationships stay available across sessions.

CodeMeridian can also derive a keyword graph on top of indexed code and documentation. That adds an explainable lexical layer for finding related docs, diagnostics, endpoints, and symbols when there is no direct structural edge.

This is especially useful for local or smaller coding models, where every token matters. CodeMeridian does not try to replace the model. It makes the model’s job smaller by giving it the smallest useful slice of architecture before it writes code.

Core usage requires no LLM API key. Optional semantic features can use local Ollama or a cloud embedding provider. The assistant is the AI; CodeMeridian is the knowledge engine.

## Why CodeMeridian?

Copilot can still read files beyond what is already open, but it has to spend context to discover them and the relationships do not persist. CodeMeridian makes that discovery explicit, cheaper, and reusable.

The graph is yours. CodeMeridian stores indexed code structure, documentation, diagnostics, and remembered project knowledge in your Neo4j instance. Nothing is sent to a CodeMeridian cloud service. If you use Copilot, Codex, Claude Code, or another hosted assistant, that assistant still has its own data handling rules, but the CodeMeridian knowledge graph itself stays under your control.

| Without CodeMeridian | With CodeMeridian |
|---------------------|------------------|
| The assistant loads files ad hoc as it searches for context | The assistant queries a graph of your entire codebase |
| Context disappears between sessions | Knowledge persists locally in Neo4j |
| "What calls this method?" requires manual searching | `find_impact` answers from the call graph |
| Refactors can miss hidden callers | Blast radius is known before edits |
| Dead code and test gaps stay invisible | `find_unreferenced` and `find_coverage_gaps` surface them |
| Large context windows get filled with noise | The agent gets the smallest useful architecture slice |
| Small local models struggle with broad repository context | Graph-backed context makes the task smaller |
| Assistants guess which model/context size is enough | Context packs include token estimates and model guidance |
| Stale indexes quietly mislead agents | Freshness and drift checks say when to re-index |
| Docs and decisions live outside the code graph | Knowledge, docs, diagnostics, and code links can be queried together |

What this gives you in practice:

- **Local ownership:** the graph and knowledge store run in your Neo4j, not a hosted CodeMeridian service.
- **Persistent memory:** architecture, docs, diagnostics, external concepts, and agent notes survive editor restarts.
- **Lower context waste:** tools return callers, callees, tests, likely files, and small snippets instead of whole-file dumps.
- **Safer edits:** impact, downstream dependencies, diagnostics, drift, and missing tests are visible before implementation.
- **Model-aware context:** `build_minimal_context` estimates token cost and suggests when a small model is enough.
- **Explainable results:** exact, heuristic, stale, and file-only matches are labeled so the assistant can say what it trusted.

## Built for agent-first workflows

AI coding agents are powerful, but they often work from incomplete or temporary context. They may search files repeatedly, miss hidden callers, forget previous discoveries, or fill their context window with noisy whole-file dumps.

CodeMeridian gives those agents a persistent graph-backed map of the repository. Instead of asking the model to rediscover the codebase every session, the agent can query CodeMeridian for the specific architecture slice it needs.

Typical workflow:

1. Start CodeMeridian locally.
2. Index your repository.
3. Connect an MCP-compatible coding agent.
4. Ask the agent to build a minimal context pack before editing.
5. Let the agent use impact, freshness, documentation, and test-coverage checks while it works.

Example prompt:

```text
Use CodeMeridian to build a minimal context pack for the authentication flow before changing login behavior.
```

## Why this helps smaller local models

Small local coding models are most useful when the context is precise. CodeMeridian helps by turning a repository into a graph-backed memory layer, so agents can retrieve the smallest useful context instead of scanning the whole codebase.

This is useful for 7B-class models, low-memory machines, and local-first workflows where context size, privacy, and repeatable code understanding matter.

Without CodeMeridian, the model may need to infer architecture from file names and ad hoc searches. With CodeMeridian, the agent can ask targeted questions:

```text
What calls this method?
Which tests cover this service?
Which frontend components use this endpoint?
What docs mention this symbol?
Is the graph fresh enough to trust?
What is the smallest context needed for this edit?
```

CodeMeridian does not make a small model magically perfect. It reduces the amount of guessing the model has to do.


## What It Indexes

CodeMeridian currently supports:

- C# via a Roslyn indexer
- TypeScript / TSX via a ts-morph indexer
- README and documentation files
- Configuration files such as `appsettings*.json`, `meridian*.json`, `.env`, and Docker Compose YAML
- **Optional vector embeddings** for semantic code similarity (find duplicate patterns, refactoring opportunities)

The indexer is designed as a language-agnostic pipeline: future language indexers can write into the same graph model and be queried through the same MCP tools. Embeddings are **opt-in** (disabled by default) and are now generated by the CodeMeridian backend, which can use local Ollama (free) or OpenAI (paid).

## Quick Start

Prerequisites:

- Docker Desktop
- .NET 10 SDK
- GitHub Copilot in VS Code
- Node.js 18+ when indexing TypeScript / TSX

Install the CLI:

```powershell
dotnet tool install -g CodeMeridian.Indexer
```

Start CodeMeridian:

```powershell
codemeridian serve
codemeridian init
```

Index this repository:

```powershell
codemeridian index . --clear
```

If you are running from a source checkout before installing the global tool:

```powershell
Copy-Item .env.sample .env
docker compose up -d
dotnet run --project tools/Indexer -- . --clear
dotnet run --project tools/Indexer -- config rebuild --project CodeMeridian
dotnet run --project tools/Indexer -- index . --skip-keywords
dotnet run --project tools/Indexer -- keywords rebuild --project CodeMeridian
```

To create a local project config and MCP client config, run:

```powershell
codemeridian init .
```

Run `codemeridian init .` again later to refresh an existing `meridian.json`. Missing defaults are merged in, the config `version` is bumped, and your existing project-specific values are preserved.

`codemeridian init .` also seeds `.meridian/architecture.json` if it does not exist, copies bundled templates from the package `architectures/` folder into `.meridian/architectures/`, and copies agent guidance into `meridian-agent-capabilities/` so repository docs stay user-owned. The bundled templates include `architecture.clean.template.json`, `architecture.onion.template.json`, `architecture.hexagonal.template.json`, `architecture.layered.template.json`, and `architecture.vertical-slice.template.json`. The active architecture file is referenced from `meridian.json` at `architecture.path` and drives `find_architecture_violations` and `find_smell_paths` after indexing.

Open this folder in VS Code or any MCP-capable client. The MCP server is registered through `.vscode/mcp.json`, and MCP-compatible clients can call CodeMeridian tools while you chat.

## Common Questions

Ask Copilot things like:

```text
Use CodeMeridian to give me an architectural overview.
```

```text
Before changing OrderService.PlaceOrderAsync, what calls it?
```

```text
Which methods have no test coverage?
```

```text
Build a minimal context pack before I change OrderService.PlaceOrderAsync.
```

```text
How is this TypeScript component connected to the backend?
```

```text
Which Newtonsoft.Json usages are safe to replace with System.Text.Json first?
```

```text
What tightly connected groups look like good extraction candidates in payments?
```

## Usage

See [usage.md](docs/usage.md) for copy-paste prompts that help AI coding assistants use CodeMeridian safely before editing.

## Core Tools

| Tool | What it does |
|------|-------------|
| `query_codebase` | Natural-language search over code structure |
| `get_architectural_overview` | Project map by namespace/module |
| `get_context_for_editing` | Compact callers/callees/interfaces context for a node |
| `build_minimal_context` | Bounded context pack with callers, callees, impact, tests, gaps, likely files, token estimate, and model guidance |
| `find_impact` | Backward blast-radius analysis |
| `find_connection` | Shortest path between two code elements |
| `find_unreferenced` | Dead-code candidates |
| `find_coverage_gaps` | Production code not called by tests |
| `find_test_shield` | Map direct test protection, indirect shields, and unshielded change-path nodes |
| `find_similar_nodes` | Find duplicate code patterns (requires embeddings enabled) |
| `hybrid_search` | Find semantically similar code near a node or subsystem boundary |
| `find_duplicate_candidates` | Review likely duplicate methods/classes with refactor-risk signals |
| `find_config_definitions` | Find where a canonical config key is defined or overridden |
| `find_config_usage` | Find which code reads or binds a canonical config key |
| `search_documentation` | Search indexed README/ADR/documentation content |
| `rebuild_keyword_graph` | Rebuild derived `Keyword` nodes and `HAS_KEYWORD` edges from indexed graph text |
| `classify_keywords` | Classify derived keywords as domain/technical/tooling/common/noise and persist usefulness scores |
| `find_related_knowledge` | Find lexically related code and docs through shared keywords |
| `find_implementation_surface` | Rank likely files and symbols to edit for a feature goal |
| `analyze_feature_implementation_path` | Map a feature request or docs/features file to implementation status, touched areas, tests, docs, missing graph evidence, and risk |
| `replace_surface` | Group dependency replacement work into safe and risky clusters before a library migration |
| `suggest_extractions` | Rank tightly connected groups that look like good extraction candidates |
| `check_graph_freshness` | Report graph confidence from indexed file, line, and timestamp metadata |
| `find_graph_drift` | Detect stale graph data before relying on exact implementation targets |
| `find_smell_paths` | Show shortest forbidden architectural dependency paths |
| `find_stale_knowledge` | Detect stale docs, weak mentions, orphaned external concepts, and orphaned code references |
| `knowledge_decay` | Alias of `find_stale_knowledge` for graph-native stale-knowledge review |
| `resolve_exact_symbol` | Resolve symbol/file/line hints to canonical node IDs before editing |
| `clear_project_knowledge` | Clear one project's indexed graph and docs before rebuilding |
| `clear_code_graph` | Clear all indexed code graph nodes while preserving docs |

Architecture rules come from the indexed project configuration when `.meridian/architecture.json` is present and indexed. If no project-specific architecture has been indexed yet, CodeMeridian falls back to the default clean-architecture template.

## Documentation

- [Installation](docs/installation.md)
- [How CodeMeridian works](docs/how-it-works.md)
- [Usage](docs/usage.md)
- [Indexing projects](docs/indexing.md)
- [Evaluating session usefulness](docs/evaluate.md)
- [Feature reference](docs/features.md)
- [Code embeddings and semantic search](docs/embeddings.md)
- [Publishing the indexer tool](docs/publishing.md)
- [Ubuntu headless deployment](docs/ubuntu-headless-deploy.md)
- [Contributor and agent guide](AGENTS.md)
- [Detailed agent docs](docs/agent/README.md)

## Keyword Enrichment

Keyword enrichment is configured in `src/McpServer/appsettings.json` under `KeywordEnrichment`.

Key options:

- `MinimumKeywordLength`: default `4`
- `AllowedShortTerms`: default includes `api`, `mcp`, `cli`, `sdk`, `jwt`, `sql`, `ast`, `ef`, `ts`
- `AdditionalStopwords`: array of project-specific terms to suppress without code changes

Typical workflow:

```text
1. Index your code and docs normally.
2. The MCP server queues incremental keyword refresh as nodes and documents are ingested.
3. Use find_related_knowledge on a node ID when you want explainable lexical matches.
4. Run rebuild_keyword_graph or classify_keywords manually when you want a full repair or rule refresh.
```

CLI equivalents:

```powershell
codemeridian config rebuild --project CodeMeridian
codemeridian keywords rebuild --project CodeMeridian
codemeridian keywords classify --project CodeMeridian
```

Configuration indexing runs as part of the normal `codemeridian index` flow. Use `--skip-config` when you only want code, docs, and diagnostics without the configuration graph. Direct config-usage extraction now works in both the Roslyn and TypeScript indexers for `process.env`, `import.meta.env`, and C# `IConfiguration` access patterns.

You can override which files count as configuration sources in `meridian.json` with `configurationFiles`, for example:

```json
{
  "configurationFiles": [".env", "appsettings.json", "appsettings.*.json", "docker-compose*.yaml"]
}
```

Keyword classification is configured under `KeywordClassification`. It can mark keywords as `Noise`, `CommonProjectTerm`, `TechnicalConcept`, `ToolingConcept`, `ArchitectureConcept`, `DiagnosticConcept`, `DomainConcept`, or `Unknown`.

## Project Layout

```text
src/
  Core/             Domain models
  Application/      Query services and orchestration
  Infrastructure/   Neo4j graph and knowledge storage
  McpServer/        MCP server and REST ingestion API
  Sdk/              Client library for ingestion
tools/
  Indexer/          Unified indexer CLI
  RoslynIndexer/    C# Roslyn indexer
  TsIndexer/        TypeScript / TSX indexer
docs/
  features.md
  how-it-works.md
  installation.md
  indexing.md
  publishing.md
```

## Status

CodeMeridian is early but usable. It already indexes C# and TypeScript/TSX projects, persists the graph in Neo4j, and exposes MCP tools for Copilot, Codex, and other compatible clients. The roadmap is tracked in [TODO.md](TODO.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines, AI-assisted development expectations, and validation steps.
