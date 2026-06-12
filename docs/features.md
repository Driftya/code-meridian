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

### `rebuild_keyword_graph`

Rebuilds a derived lexical layer over existing `CodeNode` and `KnowledgeDocument` data. The job creates shared `Keyword` nodes and `HAS_KEYWORD` relationships without changing the Roslyn or TypeScript indexers.

Keyword extraction is configurable through `KeywordEnrichment` in `appsettings.json`, including `MinimumKeywordLength`, `AllowedShortTerms`, and `AdditionalStopwords`.

```text
Rebuild the keyword graph for CodeMeridian.
```

### `classify_keywords`

Classifies derived `Keyword` nodes using configurable lexical term lists and document-frequency thresholds. Classification persists `classification`, `isCommon`, `isNoise`, `usefulnessScore`, and `classificationVersion` metadata on `Keyword` nodes so lexical matches can suppress noise and weight more useful terms higher.

Configure the rules through `KeywordClassification` in `appsettings.json`.

```text
Classify the derived keywords for CodeMeridian.
```

CLI equivalent:

```powershell
codemeridian keywords classify --project CodeMeridian
```

### Configuration graph indexing

The CLI now indexes configuration structure as part of the normal `codemeridian index` flow.

Current MVP support includes:

- `appsettings.json`
- `appsettings.*.json`
- `meridian.json`
- `meridian.sample.json`
- `.env`
- Docker Compose YAML environment sections

Configuration indexing normalizes `__` to `:` for canonical keys, preserves the raw source spelling, masks secret-like values, and links exact C# and TypeScript configuration usage back to canonical config keys.

Current code-usage extraction includes:

- C#: `IConfiguration["A:B"]`, `GetSection("A:B")`, `Configure<T>(...)`, `Bind(...)`
- TypeScript: `process.env.KEY`, `process.env["KEY"]`, `import.meta.env.KEY`, `import.meta.env["KEY"]`, env destructuring, and simple env-schema assignment patterns

CLI equivalents:

```powershell
codemeridian index . --skip-config
codemeridian config rebuild --project CodeMeridian
```

### `find_config_definitions`

Finds where a canonical configuration key is defined or overridden across indexed configuration files.

```text
Where is Neo4j:Uri defined and overridden?
```

### `find_config_usage`

Finds code nodes that directly read or bind a canonical configuration key.

```text
Which code reads or binds Neo4j:Uri?
```

### `find_related_knowledge`

Finds lexically related nodes by shared derived keywords. Results include score, shared keyword count, matched keywords, and explicit `lexical` confidence so callers can distinguish heuristic overlap from structural graph edges.

When keyword classification has been run, `find_related_knowledge` ignores keywords marked as noise and weights matches by saved usefulness score.

```text
Find docs and diagnostics related to this node through the keyword graph.
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

This now includes route-linked full-stack paths when the project has been re-indexed with the updated C# and TypeScript indexers. Static frontend HTTP calls can connect through shared `ApiEndpoint` nodes to backend ASP.NET handlers or controller actions.

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

### `find_test_shield`

Shows which tests protect a target change path.

The report separates:

- Direct shield: tests with a direct `Calls` edge to the target.
- Indirect shield: tests that protect callers/path nodes or only heuristic matches.
- Unshielded path nodes: target-adjacent callers or route nodes with no obvious test protection.

For TypeScript, direct shield detection now includes frontend test callback bodies in `.test.ts`, `.spec.ts`, and `tests` folders, so Jest and Vitest cases can protect application code even when the callback itself is anonymous.

```text
Show the test shield for OrderService.PlaceOrderAsync.
```

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

Builds a bounded context pack for one target node. The pack combines local editing context, near impact, downstream dependencies, likely files, token estimate, complexity tier, model guidance, expansion risk, optional test context, and optional source snippets.

When route-linking data exists, downstream and impact sections can include `ApiEndpoint` nodes and cross-language callers, which helps context packs cover frontend-to-backend request paths instead of only same-language structure.

The token estimate is intentionally approximate. It counts target metadata, relationship rows, summaries, likely files, optional source snippets, and relevant test context. The model guidance uses that estimate plus graph complexity signals such as affected nodes, downstream dependencies, cross-project edges, missing tests, target size, and churn.

Source snippets are disabled by default. When `includeSourceSnippets` is enabled, CodeMeridian uses bounded snippets captured by the indexer for the target and top-ranked direct dependencies, respects the remaining `maxTokens` budget, line-numbers the snippet, and truncates with a marker instead of returning whole files. The MCP server does not read project files from its own filesystem; re-index with a snippet-aware indexer version to populate this data.

When `includeTests` is enabled, test context includes:

- Direct test callers when indexed `Calls` edges exist.
- Heuristic test matches by namespace, file name, or node name similarity.
- Relevant coverage gaps near the target by same file, namespace, or exact target.

Heuristic matches are labeled explicitly so callers can distinguish them from proven call-graph relationships.

```text
Build a minimal context pack for OrderService.ProcessAsync with tests included.
```

Example guidance:

```text
Estimated: 2,400 tokens
Complexity: Low | Model guidance: Small or fast model likely sufficient
Expansion risk: Low - 2 affected nodes, 3 downstream dependencies, 1 related test
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

Ranks the most likely files, classes, and methods to edit for a feature goal. Results include canonical node IDs when available, target confidence (`exact`, `file-only`, `heuristic`, or `stale`), reasons, and indexed-metadata freshness checks so an assistant can tell whether CodeMeridian found exact targets or only broad areas.

```text
What is the best implementation surface for adding stale-knowledge detection?
```

### `resolve_exact_symbol`

Resolves a symbol, file path, and optional line hint to canonical CodeMeridian node IDs. Use it when graph search found the right file or area but an implementation step needs an exact method/class/interface ID for `get_context_for_editing`, `find_impact`, or `build_minimal_context`.

Resolution confidence is labeled as:

- `exact`: name or ID matches and freshness/line hints are usable.
- `file-only`: the file matches, but the exact symbol is inferred from nearby nodes.
- `heuristic`: the node is related, but not precise enough to edit without source verification.
- `stale`: the indexed file or line metadata no longer looks trustworthy.

```text
Resolve BuildMinimalContextAsync in src/Application/Services/CodebaseQueryService.Analytics.cs.
```

### `check_graph_freshness`

Reports trust signals for matching graph nodes using indexed file path, line metadata, update timestamp, and a confidence reason. It does not read source files from the MCP server because indexed projects may live on another machine.

```text
Can I trust the graph results for CodebaseQueryService?
```

### `find_graph_drift`

Checks whether the graph has enough indexed metadata for exact implementation work. It reports missing file metadata, incomplete line metadata, missing timestamps, drift severity, and a re-index recommendation.

```text
Should I re-index before implementing this change?
```

### `find_stale_knowledge`

Detects knowledge that may be out of date after renames, reindexing, or documentation drift. It looks for unresolved document mentions, orphaned external concepts, stale notes, and orphaned code nodes.

Documents can optionally include weak `relatedNodeIds` metadata when ingested; CodeMeridian stores that as `Mentions` edges from the document to current code nodes. The document indexer also auto-infers likely code targets from Markdown links and inline file references, so most docs do not need manual `relatedNodeIds` authoring.

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

If you already know the code nodes a document should point to, pass weak mention metadata such as `relatedNodeIds` so CodeMeridian can create explicit `Mentions` links. The document indexer also derives `relatedNodeIds` automatically from code-file links and inline file references when it can, which keeps the UX lightweight for normal docs.

The document indexer also infers weak mentions for:

- Explicit route mentions such as `POST /api/orders`, which can link docs to indexed `ApiEndpoint` nodes.
- Explicit MCP tool attribute snippets such as `[McpServerTool(Name = "find_connection")]`, which can link docs to the relevant MCP tool source files.

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

Cross-language route matching is now supported for a conservative first slice:

- ASP.NET controller and minimal API routes are indexed as `ApiEndpoint` nodes.
- TypeScript `fetch`, `axios`, and HTTP-like wrapper calls can link to those same `ApiEndpoint` nodes when the route can be resolved statically.
- Docs can mention explicit routes and MCP tool attributes so knowledge lookups can connect plans and notes to implementation surfaces.

```powershell
codemeridian index C:\Projects\MyApp\Api --project MyApp.Api --clear
codemeridian index C:\Projects\MyApp\web --project MyApp.Web --clear
```

This enables workflows like:

- Trace from a frontend component toward backend code.
- Trace from a frontend HTTP call to a backend ASP.NET endpoint through shared route nodes.
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
