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

### `find_tool_dependency_impact`

Shows which CodeMeridian tools, reports, evaluators, docs, and regression suites depend on a tool or shared contract.

This first slice is backed by an explicit application-side dependency catalog rather than persisted Neo4j nodes. It is intended to answer "if I change this tool, what else should I inspect or test?" before a feature or refactor lands.

By default it shows hard dependency edges only. Set `includeAwarenessOnly=true` to also include softer alignment risks such as report semantics or related-doc scoring that should be reviewed even when there is no direct method-call dependency.

```text
Show tool dependency impact for find_test_shield.
What depends on codemeridian evaluate-session?
List the tracked tool dependency matrix.
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
- Docker Compose YAML files such as `docker-compose.yml`, `docker-compose.yaml`, `compose.yml`, and `compose.yaml`

Configuration indexing normalizes `__` to `:` for canonical keys, preserves the raw source spelling, masks secret-like values, and links exact C# and TypeScript configuration usage back to canonical config keys.

Current code-usage extraction includes:

- C#: `IConfiguration["A:B"]`, `GetSection("A:B")`, `Configure<T>(...)`, `Bind(...)`
- TypeScript: `process.env.KEY`, `process.env["KEY"]`, `import.meta.env.KEY`, `import.meta.env["KEY"]`, env destructuring, and simple env-schema assignment patterns

CLI equivalents:

```powershell
codemeridian index . --skip-config
codemeridian config rebuild --project CodeMeridian
```

### Database tracing recognition

The C# Roslyn indexer and the TypeScript indexer can emit graph-backed database concepts as part of normal indexing:

- `DatabaseOperation` external concepts for recognized read/write call sites
- `DatabaseTable` nodes for inferred tables
- `Reads` and `Writes` edges linking code -> operation -> table

Recognition is repo-configurable through `.meridian/database-tracing.json`. The shipped starter presets cover:

- EF Core entity-set and SQL-based access
- Dapper SQL calls
- Raw SQL command execution
- Prisma model operations
- Knex table operations
- Neo4j Cypher queries

CLI equivalent:

```powershell
codemeridian trace-endpoint "POST /api/orders" --project CodeMeridian
```

### File Roles And Analysis Profiles

CodeMeridian classifies indexed files as `Source`, `Test`, `Migration`, `Snapshot`, `Generated`, `BuildArtifact`, `Documentation`, `Configuration`, or `Unknown`.

Tools use analysis profiles so each query gets the right context. Design-smell tools exclude tests, migrations, snapshots, generated files, and build artifacts by default, while test-aware tools still include indexed tests.

File role rules can be configured through `meridian.json` under `indexing.fileRoles`. If no rules are configured, CodeMeridian uses safe built-in defaults.

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

The formatter now deduplicates equivalent document/file targets, keeps stronger matches in a primary section, and collapses weaker lexical-only results into an awareness-only section instead of mixing everything into one ranked list.

```text
Find docs and diagnostics related to this node through the keyword graph.
```

## Graph Analytics

### `find_impact`

Backward blast-radius analysis. Traverses callers and transitive dependents of a method or class up to the requested depth.

When `includeConfidence` is enabled, the result also separates:

- Proven callers: structural paths without stale metadata or inferred edges.
- Heuristic callers: paths that cross abstraction edges, route-like nodes, or lower-confidence inferred edges.
- Unknown risk: stale nodes where blast radius is advisory until the graph is refreshed.

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

### `trace_endpoint`

Trace an indexed API route through graph-backed implementation, database, and messaging paths.

This consumes the graph only. Route traces rely on indexed `ApiEndpoint` nodes plus downstream `DatabaseOperation`, `DatabaseTable`, `MessageTopic`, and structural edges.

```text
Trace POST /api/orders to its tables and events.
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
- Focused verification plan: direct regression tests, contract/API forwarding tests, integration-level verification, and heuristic shield tests.
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

When `explainPaths` is enabled, the pack replaces the flat file list with file-inclusion explanations. Each entry shows why the file is included, the graph path from the target to a representative node in that file, and any nearby diagnostics or target-adjacent tests.

When route-linking data exists, downstream and impact sections can include `ApiEndpoint` nodes and cross-language callers, which helps context packs cover frontend-to-backend request paths instead of only same-language structure.

The token estimate is intentionally approximate. It counts target metadata, relationship rows, summaries, likely files, optional source snippets, and relevant test context. The model guidance uses that estimate plus graph complexity signals such as affected nodes, downstream dependencies, cross-project edges, missing tests, target size, and churn.

Source snippets are disabled by default. When `includeSourceSnippets` is enabled, CodeMeridian uses bounded snippets captured by the indexer for the target and top-ranked direct dependencies, respects the remaining `maxTokens` budget, line-numbers the snippet, and truncates with a marker instead of returning whole files. The MCP server does not read project files from its own filesystem; re-index with a snippet-aware indexer version to populate this data.

When `includeTests` is enabled, test context includes:

- Focused verification categories derived from related tests:
  direct regression tests, contract/API forwarding tests, integration-level verification, and heuristic shield tests.
- Relevant coverage gaps near the target by same file, namespace, or exact target.
- A minimal suggested `dotnet test` command when the non-heuristic recommendations collapse to one narrow seam.

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

### `architecture_drift_history`

Shows a 30-day architecture erosion timeline from current graph signals: cross-layer references, namespace cycles, and god-class growth.

```text
Show architecture drift history for CodeMeridian.
```

### `find_architecture_violations`

Finds Clean Architecture layer violations, such as `Core` depending on `Infrastructure`.

```text
Find architecture violations in CodeMeridian.
```

### `find_smell_paths`

Finds shortest forbidden dependency paths across known architectural boundaries so a violation is explainable as a graph path instead of only a flat edge.

```text
Show dependency smell paths in CodeMeridian.
```

### `find_high_churn`

Finds nodes with the highest re-index count. High churn plus high fan-in is a useful technical-debt signal.

```text
Which files change the most?
```

### `analyze_changed_subgraph`

Projects a bounded graph neighborhood around explicit changed files and turns that slice into a review-oriented risk summary.

The first slice is intentionally file-list-first rather than Git-aware:

- maps changed file paths to indexed graph nodes across mixed C# and TypeScript/TSX inputs
- ranks highest-risk changed nodes using impact breadth, related-test coverage, churn, hotspot, and architecture-smell signals
- reports nearby impacted nodes, focused verification suggestions, obvious protection gaps, and related docs or feature notes
- filters architecture violations and dependency smell paths down to findings that touch the changed slice
- suppresses structural noise when the input is docs-only or tests-only

```text
Analyze the changed subgraph for:
- src/Application/Invites/InviteService.cs
- src/Web/components/invite-panel.tsx
```

### `find_similar_nodes`

Finds semantically similar code nodes using Neo4j vector search. Requires code-node embeddings to be indexed.

```text
Find code similar to PaymentGateway.ChargeAsync.
```

### `hybrid_search`

Finds semantically similar code nodes and then filters them to a graph neighborhood around a reference node or subsystem. Tests are excluded by default.

```text
Find retry policy code near OrderService.
```

### `find_implementation_patterns`

Finds reusable implementation slices for a requested feature or flow by combining lexical or embedding seeds with graph reranking. Results explain shared structure such as entry points, application or domain behavior, contracts, repositories or stores, external boundaries, and tests.

It is language-neutral by design: the scoring works over shared graph concepts so Roslyn and TsIndexer data can participate in the same ranked pattern search.

```text
Find implementation patterns for an invite acceptance flow.
```

### `find_duplicate_candidates`

Finds duplicate-review candidates through a shared generic surface. Method/class mode compares embedded code nodes semantically. `ExternalConcept` mode clusters indexed frontend style declarations by normalized value shape so near-duplicate spacing, color, and other CSS values surface with selectors, files, normalized values, and tolerance notes.

```text
Find duplicate candidates in MyApi excluding tests.
```

### `find_frontend_cascade_conflicts`

Reports likely CSS/SCSS override conflicts from indexed frontend declarations.

It uses bounded selector specificity plus same-stylesheet source order and keeps the result explicit about confidence: findings are inferred from shared class/ID targets and indexed metadata, not claimed as full browser-proof cascade resolution.

```text
Show likely frontend cascade conflicts for Shop.Web.
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

### `analyze_feature_implementation_path`

Maps a feature request or `docs/features/*.md` path to graph-backed implementation planning evidence. The tool reports whether the feature is documented or linked, closest implementation surfaces by layer, likely touched areas, related tests, docs to update, missing graph evidence, confidence, and risk level.

The first slice composes existing graph and documentation signals rather than adding a new Feature node schema:

- full-text documentation search for feature specs and related docs
- code-node semantic/name matches from the code graph
- related test links from the closest code surfaces
- layer heuristics for Application, Infrastructure, Presentation/MCP, Core/Domain, and Tests
- freshness/confidence metadata on candidate code nodes

```text
Analyze the implementation path for docs/features/32-add-semantic-graph-hybrid-search.md.
```

### `plan_context_workflow`

Plans the recommended CodeMeridian tool sequence for an agent task. The planner is deterministic and recipe-driven. It returns JSON with the selected workflow type, ordered steps, required versus optional flags, purpose, input hints, expected output, stop conditions, execution hints, safety flags, warnings, and final response guidance.

Supported workflows include before-edit checks, feature implementation, refactor planning, responsibility slicing, architecture review, dependency replacement, knowledge health, diagnostic review, configuration review, cross-project tracing, semantic discovery, documentation ingestion, and extension-agent routing.

If `includeOptionalSteps` is omitted, narrow workflows such as `before_edit`, `diagnostic_review`, `configuration_review`, and `dependency_replacement` prune optional awareness-only steps by default. Set it to `true` to request the broader recipe or `false` for required-only steps.

```text
Plan how to use CodeMeridian before refactoring CodebaseQueryService.
```

See [Context workflows](context-workflows.md) for the full recipe list and extension guidance.

### `execute_context_workflow`

Executes an approved context workflow and returns JSON step results. This first execution slice is conservative: it runs read-only query tools exposed through the application service, refuses graph-mutating workflows unless explicitly approved, stops on missing required inputs, and reports unsupported tools instead of silently skipping them.

It uses the same workflow-aware optional-step default as `plan_context_workflow`.

```text
Execute a diagnostic review workflow for CodeMeridian with only required steps.
```

### `replace_surface`

Groups dependency replacement work into safe and risky clusters before a broad migration.

The current safe-first slice uses existing graph signals only:

- usage nodes that explicitly mention the source dependency in indexed names, summaries, namespaces, IDs, or file paths
- nearby related tests
- nearby diagnostics in the same file
- API, contract, infrastructure, and cross-project boundary hints from the local editing context
- graph freshness confidence for the indexed node

Results are grouped by module and split into:

- safe replacement groups: isolated usage with nearby tests and no strong boundary signals
- risky replacement groups: usage that crosses API/contracts/infrastructure boundaries, lacks tests, already has file-local diagnostics, or depends on stale graph metadata

```text
Which Newtonsoft.Json usages are safe to replace with System.Text.Json first?
```

### `suggest_extractions`

Ranks tightly connected production-only groups as candidate extraction targets.

The current safe-first slice combines:

- Louvain natural modules from the code graph
- hotspot fan-in signals
- god-class style size and coupling anchors
- nearby related tests from the test-shield surface
- coverage-gap signals for members that still lack protection

Results include:

- move-from location inferred from dominant namespace or path
- an anchor node that likely represents the overloaded center of the cluster
- nearby tests
- coverage-gap count
- an explainable reason string rather than a hidden extraction score

```text
What tightly connected groups look like good extraction candidates in payments?
```

### `suggest_responsibility_slices`

Suggests responsibility-based extraction slices for a large class or service.

The first deterministic slice uses existing graph evidence:

- methods contained by the target class or in the same indexed file
- shared downstream dependencies
- workflow callers such as MCP tools, endpoints, commands, controllers, and CLI surfaces
- related tests from the test-shield surface
- documentation matches from the knowledge store
- current folder and namespace patterns for the target
- advisory Louvain community memberships over the local structural neighborhood when GDS is available

Results include:

- recommended folder and namespace root
- candidate service and interface names per slice
- methods to move
- related tests or missing-test notes
- an advisory community summary that says whether a slice boundary mostly reflects callers, repositories/dependencies, tests, or workflow entry points
- migration strategy: facade-first, direct use-case replacement, or defer extraction
- warnings for stale graph data and architecture-boundary risks

```text
Suggest responsibility slices for CodebaseQueryService.
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

### `knowledge_decay`

Alias of `find_stale_knowledge` for workflows that think in terms of knowledge decay rather than stale knowledge. It returns the same graph-backed findings.

```text
Show knowledge decay in CodeMeridian.
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
