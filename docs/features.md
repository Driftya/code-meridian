# CodeMeridian - Feature Reference

This page is the detailed tool and feature reference. For the public overview and quick start, see [README.md](../README.md).

## Query and Exploration

### `query_codebase`

Natural-language search over the code graph. Ask about classes, methods, call graphs, dependencies, or structural relationships in plain English.

```text
Who calls UserService.SaveAsync?
What classes implement IRepository?
Show me the dependencies of OrderController.
```

### `get_architectural_overview`

High-level project map: namespaces/modules, class and interface counts, and key interfaces.

```text
Give me an architectural overview of MyApi.
```

### `search_documentation`

Full-text search over indexed READMEs, ADRs, comments, and documentation snippets.

```text
Why did we choose Redis over Memcached?
What is the retry strategy for external HTTP calls?
```

## Graph Analytics

### `find_impact`

Backward blast-radius analysis. Traverses callers and transitive dependents of a method or class up to the requested depth.

```text
Before I change PaymentGateway.ChargeAsync, what will break?
```

### `find_hotspots`

Ranks nodes by incoming dependencies. Useful for finding code that carries the most risk to change.

```text
Which parts of MyApi are most risky to change?
```

### `find_connection`

Finds the shortest relationship path between two nodes.

```text
How is CustomerController connected to IEmailService?
```

### `find_unreferenced`

Finds methods and classes with no incoming `Calls`, `Uses`, or `Contains` edges.

```text
What dead code can I safely delete from PaymentsService?
```

Entry points, DI-registered types, and event handlers can appear here even if they are active. Verify before deleting.

### `find_cross_project_dependencies`

Finds edges where code in one indexed project calls or depends on code in another.

```text
Show me how MyApp.Api depends on MyApp.Core.
```

### `find_coverage_gaps`

Finds production classes and methods that no test node calls.

```text
Which parts of MyApi have no test coverage?
```

Test detection is heuristic: nodes are treated as tests when their namespace or file path contains `test`.

### `find_recently_changed`

Finds nodes created or updated within a time window such as `24h`, `7d`, `2h`, or `30m`.

```text
What changed in the last 24 hours?
```

### `find_large_nodes`

Finds classes and methods that exceed configurable line-count thresholds. Works for C# and TypeScript when indexed with line-count metadata.

```text
Find methods longer than 50 lines in the payments module.
```

### `get_context_for_editing`

Returns a compact editing context block for a node: direct callers, direct callees, interfaces, file location, and size.

```text
Before I edit OrderService.ProcessAsync, give me its context.
```

Use this before `find_impact` for a quick local view. Use `find_impact` for full transitive analysis.

### `build_minimal_context`

Builds a bounded context pack for one target node. The pack combines local editing context, near impact, downstream dependencies, likely files, token estimate, and optional test context.

When `includeTests` is enabled, test context includes:

- Direct test callers when indexed `Calls` edges exist.
- Heuristic test matches by namespace, file name, or node name similarity.
- Relevant coverage gaps near the target by same file, namespace, or exact target.

Heuristic matches are labeled explicitly so callers can distinguish them from proven call-graph relationships.

```text
Build a minimal context pack for OrderService.ProcessAsync with tests included.
```

### `find_god_classes`

Finds classes that are both large and heavily depended upon, ranked by size and coupling risk.

```text
What are the god classes in MyApi?
```

### `find_downstream`

Forward blast-radius analysis. Finds nodes that the target transitively calls or depends on.

```text
What does this service depend on downstream?
```

### `find_cycles`

Detects namespace-level circular dependencies.

```text
Are there circular dependencies in this project?
```

### `find_architecture_violations`

Finds Clean Architecture layer violations, such as `Core` depending on `Infrastructure`.

```text
Find architecture violations in CodeMeridian.
```

### `find_high_churn`

Finds nodes with the highest re-index count. High churn plus high fan-in is a useful technical-debt signal.

```text
Which files change the most?
```

### `find_similar_nodes`

Finds semantically similar code nodes using Neo4j vector search. Requires code-node embeddings to be indexed.

```text
Find code similar to PaymentGateway.ChargeAsync.
```

### `find_duplicate_candidates`

Finds duplicate-code review candidates by comparing embedded method/class nodes. Supports project, namespace, node type, size, similarity, and test-exclusion filters. Results include similarity, size, fan-in risk, and direct test coverage signals.

```text
Find duplicate candidates in MyApi excluding tests.
```

### `find_diagnostics`

Finds indexed compiler, analyzer, TypeScript, and lint diagnostics for a project. Diagnostics are indexed by default unless `--skip-diagnostics` is used.

```text
What diagnostics exist in MyApi?
```

### `find_diagnostics_for_node`

Finds diagnostics in the same file as a code node, ordered by proximity to the node line.

```text
What diagnostics are near OrderService.ProcessAsync?
```

### `find_implementation_surface`

Ranks the most likely files, classes, and methods to edit for a feature goal. Results include confidence, reasons, and freshness checks so an assistant can tell whether CodeMeridian found exact targets or only broad areas.

```text
What is the best implementation surface for adding stale-knowledge detection?
```

### `check_graph_freshness`

Reports trust signals for matching graph nodes: update timestamp, whether the indexed file exists, whether the indexed line is still valid, and a confidence reason.

```text
Can I trust the graph results for CodebaseQueryService?
```

### `find_graph_drift`

Checks whether the graph has drifted from the working tree before exact implementation work. It reports missing files, invalid line ranges, missing timestamps, drift severity, and a re-index recommendation.

```text
Should I re-index before implementing this change?
```

### `find_stale_knowledge`

Detects knowledge that may be out of date after renames, reindexing, or documentation drift. It looks for unresolved document mentions, orphaned external concepts, stale notes, and orphaned code nodes.

Documents can optionally include weak `relatedNodeIds` metadata when ingested; CodeMeridian stores that as `Mentions` edges from the document to current code nodes.

```text
What CodeMeridian knowledge might be stale?
```

## Ingestion

### `ingest_code_node`

Manually add or update a code node in the graph. Useful for custom tooling or concepts the indexer does not yet extract.

### `ingest_relationship`

Manually add an edge between nodes.

### `ingest_document`

Ingest a documentation snippet so future sessions can find it with `search_documentation`.

If you already know the code nodes a document should point to, pass weak mention metadata such as `relatedNodeIds` so CodeMeridian can create explicit `Mentions` links.

### `link_external_concept`

Creates an external concept node and links it to code. Supported external concepts include:

- `DatabaseTable`
- `ApiEndpoint`
- `MessageTopic`
- `ExternalService`
- `ExternalConcept`

Supported relationship types include:

- `Reads`
- `Writes`
- `PublishesTo`
- `SubscribesTo`
- `DependsOn`
- `Calls`

### `clear_project_knowledge`

Wipes all graph and documentation data for a project context.

### `clear_code_graph`

Wipes all indexed code graph nodes and relationships across every project. Documentation nodes are preserved.

Use this after broad indexer ID/path changes or if the Neo4j graph contains stale code nodes from older indexer versions.

## Extension Agents

### `register_project_agent` / `unregister_project_agent` / `list_project_agents`

Manage external HTTP agents that CodeMeridian can route Copilot requests to.

### `call_project_agent`

Sends a question directly to a registered extension agent.

## Multi-Language Support

C# and TypeScript / TSX indexers write into the same Neo4j graph. Future language indexers can do the same by emitting the shared CodeMeridian node and edge model.

```powershell
codemeridian index C:\Projects\MyApp\Api --project MyApp.Api --clear
codemeridian index C:\Projects\MyApp\web --project MyApp.Web --clear
```

This enables workflows like:

- Trace from a frontend component toward backend code.
- Find cross-project dependencies.
- Generate architecture overviews per project or across a workspace.

## Persistence Across Sessions

All data is stored in Neo4j. Restarting VS Code, Docker, or your machine does not clear the graph.

Timestamps on nodes enable queries such as `find_recently_changed`.

## Self-Indexing

CodeMeridian can index itself:

```powershell
codemeridian index . --project CodeMeridian --clear
```

Then ask:

```text
What calls Neo4jCodeGraphRepository.UpsertNodeAsync?
Which parts of CodeMeridian have no test coverage?
What changed in CodeMeridian in the last 7 days?
```
