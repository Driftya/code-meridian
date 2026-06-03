# CodeMeridian — Features

CodeMeridian gives GitHub Copilot a **persistent, structural understanding of your codebase** — so it doesn't guess, hallucinate, or forget your architecture between sessions.

No LLM API key required. Copilot is the AI. CodeMeridian is the knowledge engine.

---

## Why CodeMeridian?

| Without CodeMeridian | With CodeMeridian |
|---------------------|------------------|
| Copilot reads files you happen to have open | Copilot queries a graph of your entire codebase |
| Context is lost when you close VS Code | Knowledge persists in Neo4j across all sessions |
| "What calls this method?" requires manual searching | `find_impact` answers instantly from the call graph |
| Refactors accidentally break callers | Blast radius is known before the first edit |
| Dead code goes unnoticed | `find_unreferenced` surfaces it on demand |
| Test gaps are invisible | `find_coverage_gaps` shows untested classes and methods |

---

## Feature reference

### Query & exploration

#### `query_codebase`
Natural-language search over your code graph. Ask about classes, methods, call graphs, dependencies, or any structural relationship — in plain English.

```
@copilot Who calls UserService.SaveAsync?
@copilot What classes implement IRepository?
@copilot Show me the dependencies of OrderController.
```

#### `get_architectural_overview`
High-level project map: namespace tree, class/interface counts, key interfaces. Copilot calls this automatically at the start of a session.

```
@copilot Give me an architectural overview of MyApi.
```

#### `search_documentation`
Full-text search over ingested READMEs, ADRs, comments, and documentation snippets. Answers "why" questions — decisions, patterns, conventions.

```
@copilot Why did we choose Redis over Memcached?
@copilot What is the retry strategy for external HTTP calls?
```

---

### Graph analytics

#### `find_impact`
**Blast radius analysis.** Traverses the call graph backwards to find every caller and transitive dependent of a method or class — up to N hops. Always run this before a refactor.

```
@copilot Before I change PaymentGateway.ChargeAsync, what will break?
```

**Example output:**
```
## Impact Analysis — PaymentGateway.ChargeAsync
14 code elements would be affected (up to 5 hops):

| Distance | Type   | Name                    | File                         |
|----------|--------|-------------------------|------------------------------|
| 1        | Method | OrderService.PlaceOrder | src/Services/OrderService.cs |
| 2        | Class  | CheckoutController      | src/Controllers/Checkout.cs  |
```

#### `find_hotspots`
**Coupling risk ranking.** Lists the nodes with the most incoming dependencies (highest fan-in) — the code that carries the most risk to touch.

```
@copilot Which parts of MyApi are most risky to change?
```

#### `find_connection`
**Path finder.** Shows the shortest relationship path between any two nodes in the graph — useful when you can't see the connection between two things.

```
@copilot How is CustomerController connected to the IEmailService?
```

#### `find_unreferenced`
**Dead code detector.** Finds methods and classes with no incoming `Calls`, `Uses`, or `Contains` edges — candidates for removal or investigation.

```
@copilot What dead code can I safely delete from PaymentsService?
```

> Note: entry points, DI-registered types, and event handlers may appear here even if active. Verify before deleting.

#### `find_cross_project_dependencies`
**Cross-boundary coupling map.** Finds edges where code in one indexed project calls or depends on code in another. Essential before extracting a module or understanding a multi-repo workspace.

```
@copilot Show me how MyApp.Api depends on MyApp.Core.
@copilot Are there any circular dependencies between our microservices?
```

#### `find_coverage_gaps`
**Test coverage blindspots.** Finds production `Class` and `Method` nodes that no test file calls. Prioritise your next test-writing session.

```
@copilot Which parts of MyApi have no test coverage?
@copilot Before I write tests, what are the highest-priority untested methods?
```

#### `find_recently_changed`
**Change scope tracker.** Finds nodes created or updated within a time window. Use this to understand what a PR touched, or to catch regressions after a deploy.

```
@copilot What changed in the last 24 hours?
@copilot Show me everything that was updated in the last 7 days in PaymentsService.
```

Supported window formats: `24h`, `7d`, `2h`, `30m`.

#### `find_large_nodes`
**SRP size smell detector.** Scans for classes exceeding 400 lines and methods exceeding 40 lines, excluding test files. Works for both C# and TypeScript codebases. Requires nodes to have been indexed with a version of the indexer that captures line counts.

```
@copilot Which classes are too large and should be split?
@copilot Find methods longer than 50 lines in the payments module.
@copilot Scan TypeScript src for SRP violations.
```

Thresholds are configurable: `classThreshold` (default 400), `methodThreshold` (default 40).

#### `get_context_for_editing`
**AI context window builder.** Given a node ID, returns a compact markdown block containing its direct callers (who will be affected), direct callees (what it depends on), interfaces it implements, and its file location and size. Designed to fit the context window of AI coding tools like Claude, Continue, and Cursor.

```
@copilot Before I edit PaymentGateway.ChargeAsync, give me its full context.
@copilot What are the callers and dependencies of OrderService.ProcessAsync?
```

Use this *before* `find_impact` for a quick local view; use `find_impact` for full transitive analysis.

#### `find_god_classes`
**Highest-risk refactoring targets.** Finds classes that are both large (SRP violation) and heavily depended upon (high fan-in). Ranked by a combined risk score. Works for both C# and TypeScript codebases.

```
@copilot What are the god classes in MyApi?
@copilot Which TypeScript classes are the most dangerous to refactor?
```

Returns a risk-rated table (Critical / High / Medium) sorted by combined size × coupling score.

---

### Ingestion

#### `ingest_code_node`
Manually add or update a node in the graph — useful for concepts that the indexer doesn't extract automatically.

```
@copilot Ingest this class as a node so I can run impact analysis on it.
```

#### `ingest_relationship`
Manually draw an edge between two existing nodes.

```
@copilot Record that OrderService depends on PaymentGateway.
```

#### `ingest_document`
Ingest a documentation snippet — README section, ADR, design decision, or any free text. Becomes searchable via `search_documentation`.

```
@copilot Ingest this ADR so it's searchable by future sessions.
```

Copilot uses this to persist its own observations:
```
@copilot Remember that PaymentGateway.ChargeAsync is called from 14 places.
```

#### `link_external_concept`
**Cross-tool knowledge weaving.** Creates a node for an external concept (database table, API endpoint, Kafka topic, external service) and draws a typed edge to an existing code node. Lets findings from other MCP tools (database introspection, API discovery) become part of the code graph.

```
@copilot Link OrderService.SaveAsync to the 'orders' database table with a Writes relationship.
@copilot Record that NotificationService publishes to the 'user-events' Kafka topic.
```

Supported external concept types: `DatabaseTable`, `ApiEndpoint`, `MessageTopic`, `ExternalService`, `ExternalConcept`.
Supported relationship types: `Reads`, `Writes`, `PublishesTo`, `SubscribesTo`, `DependsOn`, `Calls`.

After linking, `find_impact` and `find_connection` will surface these external concepts in their results.

#### `clear_project_knowledge`
Wipe all nodes and edges for a project — useful when re-indexing after a large rename or restructure.

---

### Extension agents

#### `register_project_agent` / `unregister_project_agent` / `list_project_agents`
Register an external HTTP agent as a CodeMeridian extension. Any service can expose a simple POST endpoint and become discoverable by Copilot through CodeMeridian.

```
@copilot Register the PaymentsService agent at http://payments-agent:5001/ask
```

#### `call_project_agent`
Route a question directly to a registered extension agent.

```
@copilot Ask the PaymentsService agent how invoices are generated.
```

---

## Multi-language support

Both the C# (Roslyn) and TypeScript (ts-morph) indexers write into the same Neo4j graph under different `projectContext` values. This means:

- `find_connection` can trace a path from a TypeScript React component through an API call to a C# backend method.
- `find_cross_project_dependencies` shows boundaries between your frontend and backend.
- `get_architectural_overview` can cover both together or individually.

```powershell
dotnet run --project tools/Indexer -- C:\Projects\MyApp\Api --project MyApp.Api
npx tsx tools/TsIndexer/src/index.ts C:\Projects\MyApp\web --project MyApp.Web
```

---

## Persistence across sessions

All data is stored in Neo4j on disk. When you close VS Code, reboot, or restart Docker — the graph remains. The next session picks up exactly where the last left off.

Timestamps (`createdAt`, `updatedAt`) are stored on every node so `find_recently_changed` gives you a diff across any window of time.

---

## Self-indexing

CodeMeridian indexes itself. Run:

```powershell
dotnet run --project tools/Indexer -- src --project CodeMeridian
```

Then ask Copilot questions about its own architecture:

```
@copilot What calls Neo4jCodeGraphRepository.UpsertNodeAsync?
@copilot Which parts of CodeMeridian have no test coverage?
@copilot What changed in CodeMeridian in the last 7 days?
```
