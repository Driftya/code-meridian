# AGENTS.md — CodeMeridian Contributor & Agent Guide

This file is the canonical reference for automated agents (Copilot, Claude, **Codex**, CI bots, external contributors) working in this repository. **Read this before making any structural changes.**

---

## CodeMeridian — when to call tools automatically

You have access to CodeMeridian, a persistent Neo4j code knowledge graph. Use it proactively — do not fall back to terminal file scans or manual grep when a graph tool can answer the question more accurately.

### Trigger rules

| Situation | Tool to call |
|-----------|-------------|
| Before any refactor or edit | `find_impact` with the node ID — never guess callers |
| Unfamiliar file at session start | `get_architectural_overview` for the relevant project |
| Before suggesting a deletion | `find_unreferenced` — confirm there are no hidden callers |
| "How do X and Y relate?" | `find_connection` before guessing |
| Starting work in a busy area | `find_hotspots` to understand risk |
| Writing new tests | `find_coverage_gaps` to find highest-priority untested areas |
| Reviewing a PR | `find_recently_changed` to understand scope |
| Integrating with a DB or API | `link_external_concept` to weave it into the graph |
| "Biggest class / longest file / most lines / largest method" | `find_large_nodes` — **never use a terminal file scan** |
| "God class / most coupled / hardest to change / refactor risk" | `find_god_classes` |
| Before editing any method or class | `get_context_for_editing` — callers, callees, interfaces in one call — do not grep |

### Node ID format

Node IDs follow the pattern `<Type>:<FullyQualifiedName>`, e.g.:
- `Class:MyNamespace.UserService`
- `Method:MyNamespace.UserService.SaveAsync(User,CancellationToken)`
- `Interface:MyNamespace.IRepository`

If unsure of the ID, call `query_codebase` first to find it.

### Persisting observations

When you discover something worth remembering — a pattern, a risk, an architectural decision, a bug — store it with `ingest_document` so it persists across sessions:

```
ingest_document(
  content: "<your finding>",
  source: "copilot-observation",
  projectContext: "<project name>"
)
```

Examples worth storing:
- "The `OrderService` has a hidden circular dependency with `InventoryService` via events"
- "Method `PaymentGateway.ChargeAsync` is called from 14 places — extremely high risk to change"
- "No unit tests cover the `Reporting` namespace"

Observations are searchable by future sessions via `search_documentation`.

### Coexistence with other tools

- Use CodeMeridian for: architecture, call graphs, impact analysis, size/complexity, cross-project dependencies
- Use other search tools for: runtime logs, API responses, exact file content search
- After calling an external agent via `call_project_agent`, ingest key findings with `ingest_document`

---

## Architecture overview

CodeMeridian follows **Clean Architecture**. Dependency flow is strictly one-way — outer layers depend on inner layers, never the reverse.

```
┌─────────────────────────────────────────┐
│  McpServer          (composition root)  │
│    └─ Tools         (MCP tool handlers) │
├─────────────────────────────────────────┤
│  Application        (use cases)         │
│    └─ Services      (query service)     │
│    └─ Extensions    (registry)          │
├─────────────────────────────────────────┤
│  Infrastructure     (Neo4j impl)        │
│    └─ Graph         (ICodeGraphRepo)    │
│    └─ Knowledge     (IVectorRepo)       │
├─────────────────────────────────────────┤
│  Core               (domain — NO deps)  │
│    └─ CodeGraph     (nodes, edges)      │
│    └─ Knowledge     (documents)         │
│    └─ Extensions    (agent registry)    │
└─────────────────────────────────────────┘
       Sdk            (client library)
       Indexer        (C# Roslyn CLI)
       TsIndexer      (TypeScript CLI)
```

### Dependency rules

| Layer | May depend on | May NOT depend on |
|-------|--------------|-------------------|
| `Core` | nothing | anything |
| `Application` | `Core` | `Infrastructure`, `McpServer` |
| `Infrastructure` | `Core` | `Application`, `McpServer` |
| `McpServer` | `Application`, `Core`, `Infrastructure` (via DI) | — |
| `Sdk` | nothing (standalone HTTP client) | — |

**Enforce this in code reviews.** If a `Core` class gains a reference to `Microsoft.EntityFrameworkCore` or `Neo4j.Driver`, revert immediately.

---

## Design principles

This codebase follows **SOLID**, **DRY**, **KISS**, and **Clean Code** principles. Concretely:

### Single Responsibility
- Each MCP tool class handles one concern (`CodebaseTools` = queries, `KnowledgeTools` = ingestion, `ExtensionTools` = agents).
- `CodebaseQueryService` is split into two partial classes: core queries + analytics — each file stays focused.
- `ICodeGraphRepository` exposes a clean interface; the Neo4j detail lives only in `Neo4jCodeGraphRepository`.

### Open/Closed
- New graph analytics: add a method to `ICodeGraphRepository`, implement in `Neo4jCodeGraphRepository`, add a service method in `CodebaseQueryService`, and expose as a new `[McpServerTool]`. No existing code changes required.
- New node types: add to `CodeNodeType` enum — no switches to update.

### Liskov / Interface Segregation
- `ICodeGraphRepository` and `IVectorRepository` are separate — code that needs vector search doesn't depend on graph methods.
- Tests substitute the interface via NSubstitute; no concrete classes leak into tests.

### Dependency Inversion
- All dependencies are injected via constructor. Zero `new ConcreteType()` outside of DI registration.
- MCP tool classes receive `ICodebaseQueryService` and `ICodeGraphRepository` — not the Neo4j driver.

### DRY
- Formatting helpers are private methods on the partial class, not duplicated per tool.
- `ParseWindow(string)` is a single static method used by all time-window features.
- The `TaskExtensions.WhenAll` file-scoped helper avoids nesting for parallel graph queries.

### KISS
- No mediator pattern, no event bus, no message queues. Copilot is the orchestrator; tools are simple functions.
- Neo4j queries are inline Cypher strings — readable and debuggable without an ORM layer.

---

## Key files and their purpose

| File | Purpose |
|------|---------|
| `src/Core/CodeGraph/CodeNode.cs` | Domain record for a code element. Extend this when adding new node metadata. |
| `src/Core/CodeGraph/CodeEdge.cs` | Domain record for a relationship. Add enum variants for new edge types here. |
| `src/Core/CodeGraph/ICodeGraphRepository.cs` | Contract for all graph operations. Define new queries here first. |
| `src/Infrastructure/Graph/Neo4jCodeGraphRepository.cs` | All Cypher queries. The only file that knows about Neo4j. |
| `src/Application/Services/CodebaseQueryService.cs` | Formats raw graph data into markdown for Copilot. No Neo4j here. |
| `src/McpServer/Tools/CodebaseTools.cs` | Exposes query/analytics methods as MCP tools. |
| `src/McpServer/Tools/KnowledgeTools.cs` | Exposes ingestion methods as MCP tools. |
| `src/McpServer/Tools/ExtensionTools.cs` | Extension agent management + `link_external_concept`. |
| `src/McpServer/Program.cs` | Composition root. DI wiring and Kestrel config. |

---

## Neo4j conventions

### Always consume write cursors

Every `ExecuteWriteAsync` lambda **must** call `await cursor.ConsumeAsync()` after `RunAsync()`. The Neo4j driver v5 will not commit the transaction if the cursor is not consumed.

```csharp
// CORRECT
await session.ExecuteWriteAsync(async tx =>
{
    var cursor = await tx.RunAsync(cypher, parameters);
    await cursor.ConsumeAsync();   // ← required
});

// WRONG — transaction silently rolls back
await session.ExecuteWriteAsync(async tx =>
{
    await tx.RunAsync(cypher, parameters);  // cursor never consumed
});
```

### Timestamps on all nodes

`UpsertNodeAsync` uses `ON CREATE SET n.createdAt = $now` so the first write stamps the creation time, and `SET n.updatedAt = $now` on every write. Never set `createdAt` unconditionally — that would overwrite the original value.

```cypher
MERGE (n:CodeNode {id: $id})
ON CREATE SET n.createdAt = $now
SET n.name = $name, ..., n.updatedAt = $now
```

### Index creation pattern

All schema changes go in `Neo4jInitializationService.InitializeAsync()`. Use `IF NOT EXISTS` to make the operation idempotent:

```csharp
await (await tx.RunAsync(
    "CREATE INDEX my_index IF NOT EXISTS FOR (n:CodeNode) ON (n.myField)")).ConsumeAsync();
```

### Cypher placement

All Cypher queries live in `Neo4jCodeGraphRepository.cs` as `const string` or verbatim string literals (`"""`). Never put Cypher in the Application or McpServer layers.

---

## MCP tool conventions

- **Tool names**: kebab-case — `find_impact`, `link_external_concept`.
- **Parameter names**: `camelCase` — `nodeId`, `projectContext`.
- **Return type**: `Task<string>` — always markdown-formatted for Copilot to render.
- **Empty-result path**: return a helpful guidance message, not an exception or empty string.
- **No LLM calls inside tools**: tools return facts; Copilot does the reasoning.
- **`[Description]` attribute**: required on every tool and every parameter — this is what Copilot reads to decide when to call the tool.

---

## Testing conventions

- **Framework**: xUnit + NSubstitute + FluentAssertions.
- **Test class**: `sealed` — no inheritance, no shared fixtures that mutate state.
- **Mocks**: created in the test constructor or a private factory method; never a `static` field.
- **One assertion group per test**: a single `[Fact]` tests one behaviour.
- **`[Theory]` + `[InlineData]`**: use for boundary cases (e.g. `ParseWindow` variants).
- **No integration tests here**: all tests in this repo are pure unit tests using substitutes.

### What to test

| Concern | Where |
|---------|-------|
| Service formatting logic | `CodeMeridian.Application.Tests/Services/` |
| Domain model invariants | `CodeMeridian.Core.Tests/CodeGraph/` |
| Registry behaviour | `CodeMeridian.Application.Tests/Extensions/` |
| Neo4j Cypher | Integration tests (not in this repo — run against a real Neo4j instance) |

---

## Adding a new graph analytics tool — step-by-step

1. **Define the contract** in `ICodeGraphRepository.cs`:
   ```csharp
   Task<IReadOnlyList<CodeNode>> FindMyNewQueryAsync(string? projectContext = null, CancellationToken ct = default);
   ```

2. **Implement the Cypher** in `Neo4jCodeGraphRepository.cs`:
   ```csharp
   public async Task<IReadOnlyList<CodeNode>> FindMyNewQueryAsync(string? projectContext, CancellationToken ct)
   {
       await using var session = _driver.AsyncSession();
       const string cypher = "MATCH (n:CodeNode) WHERE ... RETURN n LIMIT 100";
       var cursor = await session.RunAsync(cypher, new { projectContext = (object?)projectContext });
       var results = new List<CodeNode>();
       await foreach (var record in cursor.WithCancellation(ct))
           results.Add(MapToCodeNode(record["n"].As<INode>()));
       return results;
   }
   ```

3. **Add a service method** in `ICodebaseQueryService.cs` and `CodebaseQueryService.cs`:
   ```csharp
   Task<string> FindMyNewQueryAsync(string? projectContext = null, CancellationToken ct = default);
   ```
   Format the result as a markdown string. Return guidance if the list is empty.

4. **Expose as MCP tool** in `CodebaseTools.cs`:
   ```csharp
   [McpServerTool(Name = "find_my_new_query")]
   [Description("...what this does and when to call it...")]
   public Task<string> FindMyNewQueryAsync(...) => queryService.FindMyNewQueryAsync(...);
   ```

5. **Write unit tests** in `CodeMeridian.Application.Tests`:
   - Empty-result path
   - Happy path (correct markdown output)
   - Any edge cases

6. **Update** [README.md](README.md) tools table and [FEATURES.md](FEATURES.md).

---

## Running the project locally

```powershell
# Start Neo4j + MCP server
docker compose up -d

# Index this repo (C#)
dotnet run --project tools/Indexer -- src --project CodeMeridian

# Run all tests
dotnet test

# Rebuild MCP server container after code changes
docker compose up -d --build codemeridian-mcp
```

---

## Environment variables

| Variable | Default | Description |
|----------|---------|-------------|
| `NEO4J__URI` | `bolt://codemeridian-neo4j:7687` | Neo4j bolt URI (inside Docker network) |
| `NEO4J__USERNAME` | `neo4j` | Neo4j username |
| `NEO4J__PASSWORD` | `CodeMeridian` | Neo4j password |
| `CODEMERIDIAN_PORT` | `5100` | Host port for the MCP server |

Copy `.env.example` to `.env` to override defaults.

---

## Using CodeMeridian on this repo

Once the containers are running and the indexer has run, Copilot can answer questions like:

```
What calls Neo4jCodeGraphRepository.UpsertNodeAsync?
```
```
What is the architectural overview of CodeMeridian?
```
```
Which methods have no test coverage?
```
```
What changed in the last 7 days?
```

These work because `copilot-instructions.md` instructs Copilot to call the relevant tools automatically.
