# CodeMeridian Dogfood Follow-Up Plan

Date: 2026-07-22

Status: implemented; force-full acceptance rerun complete except final edit-route confirmation pending MCP server restart

Scope: fresh self-audit of the connected CodeMeridian tools against the CodeMeridian repository. This plan covers newly confirmed issues that remain after the completed self-tool audit in `2026-07-22-codemeridian-self-tool-audit-plan.md`.

## Outcome Summary

The tool surface is operational: graph freshness, exact-symbol resolution, direct test shielding, documentation search, configuration lookup, architecture rules, GDS execution, and focused test commands all ran successfully. Node metadata confidence is high and graph drift is low.

The graph is not yet trustworthy enough for unconstrained relationship analysis. The latest index reported 14,673 attempted local relationships, 4,753 resolved relationships, and 9,920 unresolved relationships. Dogfooding also proved several false-positive edges and downstream reports that turn those edges into confident but incorrect recommendations.

The highest-priority work is therefore graph correctness, followed by traversal constraints and report precision. Do not tune scores around corrupted relationships.

## Audit Evidence

### Live graph health

- node metadata: 10 sampled nodes at High confidence
- relationship completeness: Medium
- last incremental C# index: 2026-07-22 16:51:24Z
- last full relationship index: unknown
- graph drift: low; no sampled missing paths, lines, hashes, or timestamps
- configured architecture violations: none
- indexed diagnostics: 74 warnings

### Focused verification baseline

- Roslyn indexer focus: 15 passed, 0 failed
- Application analytics/keyword focus: 36 passed, 0 failed
- Neo4j endpoint/coverage focus: 4 passed, 0 failed
- TypeScript indexer: 63 passed, 0 failed

These green tests do not cover the negative dogfood scenarios below.

### Representative tool results that worked

- `query_codebase` returned deterministic ambiguity for the two `DeleteDiagnosticsAsync` methods.
- `resolve_exact_symbol` returned canonical interface, implementation, and regression-test IDs.
- `get_context_for_editing`, `find_impact`, `find_test_shield`, and `find_connection` found the new direct integration test.
- `find_config_definitions` found `analysis:ranking:productionOnlyByDefault` in `meridian.json`.
- `find_config_usage` found `CodeMeridian:Analysis` reads and binding in dependency injection.
- `search_documentation` found the prior audit, indexing guidance, and diagnostics feature note.
- `find_related_knowledge` worked when called with a canonical `sourceNodeId`.
- PageRank, betweenness, bridge, Louvain, and extraction queries executed without a GDS availability failure.

## Confirmed Findings

| ID | Priority | Finding | Evidence | Status |
|---|---:|---|---|---|
| CM-DOG-001 | P0 | C# call resolution creates false local calls when an external or unknown receiver happens to match one local method by name and arity | `tx.RunAsync(cypher, parameters)` in `DeleteDiagnosticsAsync` resolves to `IndexWatchLoop.RunAsync`; unrelated `.Add(string)` calls inflate `EdgeResolutionResult.Add` to fan-in 187 | Implemented |
| CM-DOG-002 | P0 | Endpoint tracing can leave the route's execution slice through undirected `Contains` and abstraction edges, then reach arbitrary database concepts | diagnostics DELETE route returned 20 unrelated tables including `Calls`, `Members`, `Steps`, and configuration-analysis fields | Implemented |
| CM-DOG-003 | P1 | C# database tracing treats ordinary in-memory member collections as EF Core tables | application DTO properties such as `Callers`, `Callees`, and `Members` were indexed as `EFCore Reads/Writes` tables | Implemented |
| CM-DOG-004 | P1 | Nested package test helpers can be persisted as production code | `tools/TsIndexer/tests/walker-test-helpers.ts` is stored as Source because defaults match root `tests/**` but not `**/tests/**`; hotspots then list it under Production candidates | Implemented |
| CM-DOG-005 | P1 | Coverage gaps report a class as untested even when tests call its contained methods | `CodebaseQueryService` is a high-priority gap despite hundreds of member-level test calls | Implemented |
| CM-DOG-006 | P1 | Betweenness output does not preserve score order | ranks 17-19 have scores larger than several ranks 2-16 because display penalty is applied before score | Implemented |
| CM-DOG-007 | P1 | Namespace cycles are duplicated, include test-only coupling, and render a literal `?` instead of a direction marker | live output contains both A/B and B/A, production/test namespace pairs, and `A?B` headings | Implemented |
| CM-DOG-008 | P1 | Responsibility slicing collapses unrelated methods into enormous, misleading slices | `CancellationToken` causes the term `token` to classify 114 methods as `ContextPacks`; all six slices cite the same community | Implemented |
| CM-DOG-009 | P1 | Partial C# type metadata is overwritten per declaration instead of aggregated | the 10,262-line `CodebaseQueryService` is represented as a 160-line class in one arbitrary partial file, distorting large-node, god-class, and slice reports | Implemented |
| CM-DOG-010 | P2 | Implementation-surface and edit-route tools can miss an exact concrete implementation even when the goal names its behavior | diagnostics cleanup goal promoted `ICodeGraphRepository.cs`; edit route anchored on `FindImpactPathsAsync` and omitted the changed implementation and its direct regression test | Implemented |
| CM-DOG-011 | P2 | Low-drift output recommends a destructive clear reindex even when all node metadata signals are complete | the only degraded signal was Medium relationship trust, but the recommendation was `codemeridian index ... --clear` | Implemented |
| CM-DOG-012 | P2 | Related-knowledge confidence is inflated by ubiquitous language/framework tokens | unrelated query methods rank High through shared `cancellation`, `token`, `graph`, and `repository` terms | Implemented |

## Phase 0: Add Failing Regression Fixtures

All fixes start with focused tests that reproduce the live false positives.

### 0.1 Receiver-safe C# call resolution

Primary files:

- `tools/RoslynIndexer/Pipeline/CSharpAstWalker.cs`
- `tools/RoslynIndexer/Pipeline/CSharpCallEdgeResolver.cs`
- `tests/CodeMeridian.RoslynIndexer.Tests/Pipeline/CSharpIndexerTests.cs`

Add fixtures proving:

- `tx.RunAsync(string, object)` does not resolve to an unrelated local `RunAsync` method
- `list.Add(string)` does not resolve to a unique domain method named `Add(string)`
- unresolved external receiver calls are counted with an explicit reason
- known local receiver types still resolve correctly
- `this.Method()`, typed parameters, typed locals, and typed fields retain their existing behavior

### 0.2 Constrained endpoint tracing

Primary files:

- `src/Infrastructure/Graph/Neo4jCodeGraphRepository.EndpointTracing.cs`
- `tests/CodeMeridian.Infrastructure.Integration.Tests/Neo4jCodeGraphRepositoryFindEndpointTracesIntegrationTests.cs`

Extend the Neo4j fixture with two methods in the same implementation class: one belongs to the endpoint flow and one reads an unrelated table. Assert that only the endpoint-owned table is returned. Add an interface with multiple implementation members to prove the trace cannot cross `Implements -> Class -> Contains -> unrelated member`.

### 0.3 Database tracing negative fixtures

Primary files:

- `tools/RoslynIndexer/Pipeline/CSharpDatabaseTracingExtractor.cs`
- `tests/CodeMeridian.RoslynIndexer.Tests/Pipeline/CSharpDatabaseTracingExtractorTests.cs`

Add negative tests for ordinary objects named `context`, `db`, or `tx` whose collection properties use `Count`, `Add`, `Remove`, or LINQ. No `DatabaseOperation` or `DatabaseTable` nodes should be emitted without stronger database-type or configured-preset evidence.

### 0.4 Analytics regression fixtures

Add focused tests for:

- a tested method contained by a class suppresses the class-level coverage gap
- betweenness ranks strictly by score inside each visible actionability section
- cycles emit one canonical unordered namespace pair and exclude test-role nodes by default
- cycle markdown contains a real direction/bidirectional marker, not `?`
- responsibility slicing does not treat `CancellationToken` as context-pack evidence
- overly broad or low-cohesion slices return `defer_extraction`
- a diagnostics-cleanup goal promotes the concrete repository method and direct regression test
- low node drift plus Medium relationship trust does not recommend `--clear`

### 0.5 Monorepo file-role fixtures

Primary files:

- `src/Application/Services/IndexedFileRoleOptions.cs`
- `tests/CodeMeridian.Application.Tests/Services/FileRolePolicyTests.cs`
- `tests/CodeMeridian.Indexer.Tests/Cli/IndexCommandHandlerTests.cs`

Cover nested roots such as:

- `packages/orders/tests/helpers.ts`
- `tools/TsIndexer/tests/walker-test-helpers.ts`
- `services/api/test/fixtures.ts`
- equivalent C#, TS, TSX, JS, and JSX paths

Verify the role survives batch-file serialization and ingestion.

## Phase 1: Repair Graph Construction

### 1.1 Require trustworthy receiver evidence

Do not select the sole same-name/arity candidate for a member-access call when the receiver is present but unresolved. Persist whether the syntax was:

- unqualified/local invocation
- `this`/`base` invocation
- statically typed local, parameter, field, or property receiver
- type-qualified static invocation
- unknown/external member receiver

Only use the unique-candidate fallback for unqualified calls where local resolution is plausible. For an unknown member receiver, leave the edge unresolved rather than manufacturing a local call.

Prefer Roslyn semantic-model symbols when a compilable project is available. Keep the syntax-only resolver as a conservative fallback for loose files.

### 1.2 Strengthen database-provider evidence

For EF Core recognition, require at least one of:

- semantic receiver type derives from or is configured as `DbContext`/`DbSet<T>`
- receiver member has a known `DbSet<T>` type
- SQL text passes the existing SQL parser
- an explicit repository database-tracing preset authorizes the receiver/type pattern

Receiver text alone must not create a database table from an arbitrary collection property. Persist recognition confidence and evidence so trace tools can exclude weak operations by default.

### 1.3 Fix nested file-role defaults

Add `**/tests/**`, `**/test/**`, and `**/__tests__/**` defaults for supported languages. Ensure explicit configured roles still override defaults. When a stored role conflicts with an unmistakable test path, surface graph drift instead of silently presenting the node as production.

### 1.4 Represent partial types deterministically

Aggregate all declarations for the same canonical C# type during the full resolution catalog pass. Persist:

- declaration count
- deterministic declaration paths
- aggregate line count
- aggregate content hash
- a deterministic primary location for display only

Recompute the aggregate when a partial file changes or is deleted. Do not let ingestion order choose the class file, line count, or hash. Keep method nodes attached to the shared canonical type.

## Phase 2: Constrain Structural Analysis

### 2.1 Make endpoint traces execution-direction aware

Replace the undirected broad shortest path with a bounded stateful route:

1. resolve the endpoint to its handler through the known endpoint edge direction
2. traverse forward through behavioral edges such as `Calls`, `Uses`, `Reads`, `Writes`, `PublishesTo`, and `SubscribesTo`
3. permit `Implements` only to resolve the called contract member to its implementation member
4. require a `DatabaseOperation` before a `DatabaseTable`
5. never use class/namespace `Contains` to jump between sibling members

Return path confidence and omit weak database operations unless the caller explicitly requests inferred paths.

### 2.2 Make coverage class-aware

A class is covered when a test directly calls/uses the class or calls one of its contained methods. Keep method-level gaps independent so an untested method still appears. Add separate labels for:

- no class/member test evidence
- class has some tested members but this method is untested
- only heuristic filename/namespace test evidence

### 2.3 Canonicalize cycle analysis

Scope both directions to the selected project. Exclude test, generated, migration, snapshot, configuration, and build-artifact roles by default. Return each unordered pair once using a stable lexical key. Report the edge evidence that proves both directions and use a valid `↔` marker in markdown.

## Phase 3: Repair Ranking And Planning Precision

### 3.1 Preserve betweenness ordering

Use the same metric-aware actionability partition used by PageRank, hotspots, churn, and downstream distance. Bucket first, then order betweenness descending inside each bucket. Add monotonic-order assertions.

### 3.2 Add responsibility-slice cohesion gates

Remove framework/signature terms such as `token`, `cancellation`, `async`, `task`, and primitive types from responsibility naming. Prefer method-name domain terms, shared non-generic dependencies, workflow callers, tests, and docs.

Set configurable safeguards:

- maximum methods per proposed slice
- minimum pairwise/shared-evidence ratio
- maximum share of the target class assigned to one slice
- minimum distinct community evidence before labeling confidence High

If one generic term captures most methods or all slices cite the same undifferentiated community, return `defer_extraction` with an explanation.

### 3.3 Promote exact behavioral targets

For `find_implementation_surface`, `analyze_feature_implementation_path`, and `plan_edit_route`:

- reward exact method-name and source-snippet goal matches above broad interface terms
- follow `Implements` from a matched contract to concrete implementations
- include direct tests of the selected concrete symbol
- do not invent a contract-edit step when behavior changes behind an unchanged interface
- reject anchors whose symbol terms do not materially overlap the goal

Add a shared acceptance fixture for the diagnostics-cleanup scenario so the three tools remain aligned.

### 3.4 Make freshness recommendations match the failing dimension

Render separate node and relationship remediation:

- missing node IDs/paths/hashes after schema or ID changes: consider `--clear`
- missing full relationship baseline: run a supported non-destructive full relationship index
- unresolved relationships: show counts/reasons and point to indexer diagnostics
- stale documents only: reindex documents

Never recommend a destructive clear solely because relationship confidence is Medium.

### 3.5 Reduce lexical confidence from ubiquitous tokens

Extend keyword classification with language/framework stop terms and document-frequency evidence. At minimum review `cancellation`, `token`, `async`, `task`, primitive types, and repository-wide infrastructure nouns. High lexical confidence should require at least one discriminating keyword; otherwise place the match in awareness-only results.

## Phase 4: Reindex And Dogfood Acceptance

Run a clean, current-version self-index only after graph-construction fixes are complete. A clear reindex is appropriate here because canonical relationship and partial-type representation will change.

### Required commands

```powershell
dotnet test tests/CodeMeridian.RoslynIndexer.Tests/CodeMeridian.RoslynIndexer.Tests.csproj -c Release
dotnet test tests/CodeMeridian.Application.Tests/CodeMeridian.Application.Tests.csproj -c Release
dotnet test tests/CodeMeridian.Infrastructure.Integration.Tests/CodeMeridian.Infrastructure.Integration.Tests.csproj -c Release
dotnet test -c Release
npm test --prefix tools/TsIndexer
codemeridian index . --project CodeMeridian --clear
codemeridian evaluate-session . --project CodeMeridian
```

### Dogfood acceptance matrix

| Scenario | Required result |
|---|---|
| `get_context_for_editing` on `DeleteDiagnosticsAsync` | no `IndexWatchLoop`, scheduler, GDS, or keyword-repository paths |
| `find_hotspots` | `EdgeResolutionResult.Add` is not inflated by unrelated collection calls; test helpers are absent from Production candidates |
| `trace_endpoint DELETE /project/{param}/diagnostics` | only diagnostics-cleanup paths; no unrelated application-analysis tables |
| `find_coverage_gaps` | `CodebaseQueryService` is not called wholly untested when member tests exist |
| `get_betweenness` | scores descend monotonically within each visible section |
| `find_cycles` | one row per unordered production namespace pair; no reciprocal duplicates or `?` marker |
| `suggest_responsibility_slices CodebaseQueryService` | bounded coherent slices or explicit defer; no 100+ method ContextPacks slice |
| `find_large_nodes` / `find_god_classes` | partial types use deterministic aggregate size and location evidence |
| diagnostics cleanup implementation goal | concrete repository method and direct integration test are primary targets |
| `find_graph_drift` with Medium relationship trust only | non-destructive relationship-index guidance |
| `find_related_knowledge` for diagnostics cleanup | discriminating diagnostics/index-run matches rank above generic cancellation-token peers |

## Definition Of Done

- [x] Unknown external/member receivers cannot create local C# call edges through name/arity uniqueness alone.
- [x] Database tracing does not infer tables from ordinary in-memory collections.
- [x] Endpoint traces cannot traverse through unrelated sibling members.
- [x] Nested monorepo test helpers are classified and displayed as tests.
- [x] Class-level coverage considers tested contained members without hiding untested methods.
- [x] Betweenness is metric-monotonic inside actionability sections.
- [x] Cycle output is canonical, production-scoped, evidence-backed, and correctly formatted.
- [x] Responsibility slices meet explicit cohesion/size gates or defer safely.
- [x] Partial type size, hash, and location metadata are deterministic aggregates.
- [x] Implementation-surface, feature-path, and edit-route outputs agree on exact behavioral targets.
- [x] Drift guidance does not recommend destructive cleanup for relationship-only uncertainty.
- [x] Related-knowledge High confidence requires discriminating lexical evidence.
- [ ] Focused regressions, full .NET tests, TypeScript tests, clean reindex, and every dogfood acceptance scenario pass.

## Implementation Evidence

- Full .NET suite after acceptance follow-up fixes: 911 passed, 0 failed.
- TypeScript indexer suite: 63 passed, 0 failed.
- Focused regressions cover all twelve findings, including live Neo4j endpoint, coverage, and cycle fixtures.
- The current repository CLI classified 413 C# files as Source=232, Test=168, Configuration=13, with no Unknown roles.
- Its relationship diagnostics left 1,568 unknown member receivers explicitly unresolved.
- A user-completed clean `--clear` self-index restored the graph. Freshness sampled 20 High-confidence nodes, drift was low, and the full index completed at 2026-07-23 08:48:49Z.
- Eight acceptance scenarios passed directly: receiver-safe editing context, constrained endpoint trace, class-aware coverage, monotonic betweenness, canonical cycles, responsibility deferral, aggregate partial-type sizing, and non-destructive relationship remediation.
- The fresh graph exposed three remaining precision gaps: a stored Source role overrode an unmistakable TypeScript test path, edit-route test provenance was lost when stored role metadata was stale, and `infrastructure` still inflated lexical confidence. These now have focused regression coverage and passing fixes.
- A user-completed force-full self-index finished at 2026-07-23 09:50:35Z. Freshness sampled 20 High-confidence nodes and graph drift remained low with no stored-role conflicts.
- The force-full rerun confirmed corrected hotspot suppression and diagnostics-focused related knowledge. The diagnostics regression test ranked sixth and generic non-diagnostics repository methods were pruned.
- The rerun exposed one final repository predicate gap: `FindRelatedTestsAsync` treated a stored Source method containing `test` in its name as a related test. Stored roles are now authoritative, with a live Neo4j integration regression; focused integration tests passed 3/3 and the full .NET suite passed 911/911.
- Final live edit-route confirmation requires restarting the connected MCP server so it loads the corrected repository query. No additional reindex is required for this query-only fix.

## Non-Goals

- Do not refactor large services merely because `find_large_nodes` reports them.
- Do not tune GDS thresholds before false edges are removed.
- Do not weaken conservative unknown/Medium relationship warnings.
- Do not hardcode CodeMeridian namespaces, paths, method names, or project-specific database concepts in production logic.
