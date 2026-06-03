# CodeMeridian

A persistent code knowledge graph that gives GitHub Copilot a grounded, structural understanding of your codebase. It acts as the **deterministic context layer** — so Copilot doesn't drift or forget your architecture when working on large projects.

No LLM API key required. Copilot is the AI; CodeMeridian is the knowledge engine.

**→ [See all features in FEATURES.md](FEATURES.md)**  
**→ [Contributor & agent guide in AGENTS.md](AGENTS.md)**  
**→ [Ubuntu headless deployment guide](docs/ubuntu-headless-deploy.md)**

---

## How it works

```
Your codebase
     │
     ▼
[Indexer CLI]  ──── Roslyn AST walk ────►  [Neo4j Graph]
                                                  │
                                           [MCP Server :5100]
                                                  │
                                          ◄── SSE ──┤
                                        VS Code Copilot
```

1. **Indexer** — a CLI tool that walks your C# source with Roslyn, extracts classes, methods, interfaces, call graphs, and dependencies, then pushes them into the graph database.
2. **Neo4j** — stores the structural knowledge graph. Persists across sessions.
3. **MCP Server** — exposes the graph as tools Copilot can call autonomously. Runs as a Docker container.
4. **VS Code** — connects to the MCP server automatically when you open this folder (via `.vscode/mcp.json`). Copilot then calls the tools when it needs context.

---

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- GitHub Copilot extension in VS Code (any plan)

---

## Start CodeMeridian

### 1. Copy the environment file

```powershell
Copy-Item .env.example .env
```

Edit `.env` if you want a custom Neo4j password (optional — the default works fine).

For public access, set `CodeMeridian_Auth_ApiKey` in `.env`. When this value is set, CodeMeridian requires clients to send:

```http
Authorization: Bearer <your-api-key>
```

The C# and TypeScript indexers automatically read `.env` from this repository root, so local indexing commands use the same server URL and auth key as Docker Compose.

### 2. Start the containers

```powershell
docker compose up -d
```

This starts:
- **Neo4j** on `http://localhost:47474` (browser) / `bolt://localhost:47687`
- **MCP Server** on `http://localhost:5100/sse`

Wait ~30 seconds for Neo4j to initialize on the first run.

### 3. Verify

Open `http://localhost:47474` in a browser. Login with `neo4j` / `CodeMeridian` (or your custom password). The graph is empty until you index a project.

---

## Register your first project (index a codebase)

Run the combined Indexer CLI against any C# / TypeScript solution or folder. It scans the target directory first, then runs the C# indexer when `.cs` files exist and the TypeScript indexer when `.ts` or `.tsx` files exist.

```powershell
dotnet run --project tools/IndexerAll -- <path-to-your-project>
```

If you omit the path, it indexes the directory where you ran the command:

```powershell
dotnet run --project J:\Projects\driftya-solutions\code-meridian\tools\IndexerAll
```

**Example — index itself:**
```powershell
dotnet run --project tools/IndexerAll -- .
```

**Example — index another project on your machine:**
```powershell
dotnet run --project tools/IndexerAll -- C:\Projects\MyApi
```

**Example - keep indexing while you work:**
```powershell
dotnet run --project tools/IndexerAll -- C:\Projects\MyApi --watch
```

**Options:**
| Flag | Description |
|------|-------------|
| `--project <name>` | Short name for this codebase in the graph. Auto-detected from `package.json`, `.sln` / `.slnx` / `.code-workspace`, or folder name if omitted |
| `--url <url>` / `--CodeMeridian <url>` | MCP server URL. Default: `CodeMeridian_Url` from `.env`, or `http://localhost:5100` |
| `--clear` | Wipe this project's existing graph data before indexing. Applied once when both indexers run |
| `--no-docs` | Skip README / documentation ingestion |
| `--watch` | Stay running and re-index. If both C# and TypeScript files exist, C# watch mode runs first |

Re-run the Indexer whenever you make significant structural changes.

---

### Indexing a TypeScript / JavaScript project

A separate Node.js indexer handles TypeScript and TSX. It uses [ts-morph](https://ts-morph.com/) (TypeScript Compiler API) to extract the same structural graph — files, classes, interfaces, methods, properties, enums, import dependencies, inheritance and implementation edges.

**Prerequisites:** Node.js 18+

**First time only — install dependencies:**
```powershell
cd tools/TsIndexer
npm install
cd ../..
```

**Run it:**
```powershell
npx tsx tools/TsIndexer/src/index.ts <path-to-your-ts-project>
```

**Example — index a React app:**
```powershell
npx tsx tools/TsIndexer/src/index.ts C:\Projects\my-react-app
```

**Example — index a Node API:**
```powershell
npx tsx tools/TsIndexer/src/index.ts C:\Projects\my-api
```

**Example — index itself:**
```powershell
npx tsx tools/TsIndexer/src/index.ts tools/TsIndexer/src
```

**Example — index with a custom server URL:**
```powershell
npx tsx tools/TsIndexer/src/index.ts C:\Projects\my-api --url http://localhost:5100
```

**Example — wipe and re-index:**
```powershell
npx tsx tools/TsIndexer/src/index.ts C:\Projects\my-api --clear
```

**Example - keep indexing while you work:**
```powershell
npx tsx tools/TsIndexer/src/index.ts C:\Projects\my-api --watch
```

**Example — both C# backend and TS frontend in the same graph:**
```powershell
dotnet run --project tools/Indexer -- C:\Projects\MyApp\Api
npx tsx tools/TsIndexer/src/index.ts C:\Projects\MyApp\web
```

**Options:**
| Flag | Description |
|------|-------------|
| `--project <name>` | Short name for this codebase in the graph. Auto-detected from `package.json` name, `.code-workspace` filename, or folder name if omitted |
| `--url <url>` | MCP server URL. Default: `CodeMeridian_Url` from `.env`, or `http://localhost:5100` |
| `--clear` | Wipe this project's existing graph data before indexing |
| `--no-docs` | Skip README ingestion |
| `--watch` | Stay running and re-index when `.ts`, `.tsx`, or `.md` files change |

**What it extracts:**
| Node type | What it is |
|-----------|-----------|
| `File` | Each `.ts` / `.tsx` file |
| `Class` | Classes (including React components) |
| `Interface` | TypeScript interfaces |
| `Method` | Class methods and standalone functions |
| `Property` | Class properties |
| `Enum` | TypeScript enums |

**Edges extracted:**
| Relationship | Meaning |
|-------------|---------|
| `DependsOn` | Import statement (`import … from '…'`) |
| `Inherits` | `extends` |
| `Implements` | `implements` |
| `Contains` | File → Class / Method |

---

## How MCP auto-registers in VS Code

When you open this folder in VS Code, the file `.vscode/mcp.json` is picked up automatically by the GitHub Copilot extension:

```jsonc
{
  "servers": {
    "CodeMeridian": {
      "type": "sse",
      "url": "http://localhost:5100/sse",
      // Required only when CodeMeridian_Auth_ApiKey is set in .env / your shell.
      "headers": {
        "Authorization": "Bearer ${env:CodeMeridian_Auth_ApiKey}"
      }
    }
  }
}
```

**No manual setup needed.** As long as the MCP server container is running, Copilot will:
- Show `CodeMeridian` as a connected MCP server in the Copilot panel
- Autonomously call `query_codebase`, `get_architectural_overview`, and `search_documentation` when relevant to your chat messages

> **To use it in another project:** copy `.vscode/mcp.json` into that project's `.vscode/` folder. As long as CodeMeridian is running, Copilot in that project will have access to the same graph.

---

## Using Copilot with CodeMeridian

Just chat normally in Copilot. It will call the tools on its own when it needs context. You can also be explicit:

```
@copilot Use CodeMeridian to give me an architectural overview of MyApi
```
```
@copilot Before changing anything, check what calls UserService.SaveAsync
```
```
@copilot Search the docs for why we chose the current retry strategy
```

**Available tools Copilot can call:**

**Query & exploration**

| Tool | What it does |
|------|-------------|
| `query_codebase` | Find classes, methods, call graphs, dependencies by natural language |
| `get_architectural_overview` | High-level namespace/module map of a project |
| `search_documentation` | Full-text search over ingested READMEs, ADRs, comments |

**Graph analytics**

| Tool | What it does |
|------|-------------|
| `find_impact` | Find everything that calls a given node (up to N hops) — safe-to-change analysis |
| `find_hotspots` | Rank nodes by fan-in — the highest-risk code to touch |
| `find_connection` | Find how two nodes are connected through the graph |
| `find_unreferenced` | Find dead code — nodes with no incoming references |
| `find_cross_project_dependencies` | Find edges that cross project boundaries — coupling between services |
| `find_coverage_gaps` | Find production classes/methods with no test calling them |
| `find_recently_changed` | Find nodes created or updated within a time window (e.g. `"24h"`, `"7d"`) |
| `find_large_nodes` | Find classes (>400 lines) and methods (>40 lines) violating SRP — C# and TypeScript |
| `get_context_for_editing` | Compact callers / callees / interfaces context block for AI coding tools |
| `find_god_classes` | Find classes that are both large and heavily coupled — highest refactor risk |

**Ingestion**

| Tool | What it does |
|------|-------------|
| `ingest_code_node` | Add or update a node in the graph manually |
| `ingest_relationship` | Add an edge between nodes manually |
| `ingest_document` | Ingest a documentation snippet (README, ADR, comment) |
| `link_external_concept` | Link a code node to an external concept (DB table, API endpoint, Kafka topic) |
| `clear_project_knowledge` | Wipe a project's graph data |

**Extension agents**

| Tool | What it does |
|------|-------------|
| `list_project_agents` | List registered extension agents |
| `register_project_agent` | Register an external agent as an extension |
| `unregister_project_agent` | Remove a registered extension agent |
| `call_project_agent` | Delegate a task to a registered extension agent |

---

## Stop / reset

```powershell
# Stop containers (keeps graph data)
docker compose down

# Stop and wipe all graph data (full reset)
docker compose down -v
```

---

## Project layout

```
src/
  Core/          Domain models — CodeNode, CodeEdge, KnowledgeDocument
  Application/   Query orchestration — CodebaseQueryService
  Infrastructure/  Neo4j implementations
  McpServer/     MCP server — the tools Copilot calls
  Sdk/           Client library for pushing data into CodeMeridian from other projects
tools/
  Indexer/       C# Roslyn CLI indexer
  TsIndexer/     TypeScript/TSX indexer (ts-morph, Node.js)
tests/
  CodeMeridian.Core.Tests/
  CodeMeridian.Application.Tests/
.vscode/
  mcp.json       ← auto-registers this MCP server when VS Code opens this folder
docker-compose.yml
```

---

## SDK — push data from your own project

If you want to ingest data programmatically (CI pipeline, custom tooling):

```csharp
// In your project's DI setup
services.AddCodeMeridianClient("http://localhost:5100");

// Inject and use
public class MyService(ICodeMeridianClient client)
{
    await client.IngestDocumentAsync("MyApi", "ADR-001", "We use MediatR for CQRS because...");
    await client.ClearProjectKnowledgeAsync("MyApi");
}
```

Add the SDK reference:
```xml
<ProjectReference Include="path/to/src/Sdk/CodeMeridian.Sdk.csproj" />
```
