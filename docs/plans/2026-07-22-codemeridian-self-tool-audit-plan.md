# CodeMeridian Self-Tool Audit Remediation Plan

- Status: completed
- Date: 2026-07-22
- Completed: 2026-07-22
- Scope: CodeMeridian graph/indexer correctness, MCP tool precision, test coverage, dependency security, and test-suite maintainability
- Original audit mode: analysis and planning only; remediation implementation and verification are now complete
- Assumption: the requested `docs/plan` location is intentional even though older plans live under `docs/plans`
- Primary principle: repair graph correctness before tuning downstream reports, because coverage, impact, test-shield, hotspot, and implementation-surface tools all depend on trustworthy nodes and relationships
- Portability constraint: production behavior is project-agnostic; fixes use language/file conventions, project configuration, graph metadata, and generic test naming rather than CodeMeridian-specific names or paths

## Executive Summary

The current repository builds and its complete local test suites pass, but dogfooding CodeMeridian against its own graph exposed one high-impact indexing defect and several downstream correctness or precision problems that the current tests do not catch.

The highest-priority defect is incremental C# indexing. The incremental pipeline deletes and reindexes only changed files, while `CSharpCallEdgeResolver` can resolve calls only against nodes extracted in the current batch. A call from a changed file to an unchanged file can therefore disappear. The inverse case is also unsafe: when a callee changes, incoming edges from unchanged callers can be deleted with the old target and are not recreated. This directly corrupts tools that rely on incoming or outgoing relationships.

This defect is visible in the live CodeMeridian graph:

- `find_test_shield` found three direct tests for `FindHotspotsAsync`, but found no direct tests for `FindCoverageGapsAsync`, `FindDuplicateCandidatesAsync`, `QueryStructureAsync`, or `QueryCodebaseAsync` even though those tests exist and call the targets in source.
- `build_minimal_context` showed `FindCoverageGapsAsync` calling itself instead of its repository overload and did not find its direct tests.
- `find_connection` could connect missing test/target pairs only through unrelated sibling methods and `Contains` edges.
- `check_graph_freshness` still reported high node confidence and `find_graph_drift` reported low drift, demonstrating that current freshness checks do not measure relationship completeness.

Two separate high-severity dependency advisories were also confirmed:

- transitive `Microsoft.OpenApi` 2.0.0 through `Microsoft.AspNetCore.OpenApi` 10.0.10; patched OpenAPI.NET lines begin at 2.7.5 or 3.5.4: <https://github.com/advisories/GHSA-v5pm-xwqc-g5wc>
- transitive `brace-expansion` 5.0.6 through `minimatch` and `ts-morph`; the 5.x patched version is 5.0.7: <https://github.com/advisories/GHSA-3jxr-9vmj-r5cp>

The recommended order is:

1. add failing regression tests for incremental relationship loss
2. fix dependency advisories and add audit gates
3. make incremental C# relationship reconstruction correct
4. teach freshness/drift tools to report relationship confidence separately from node freshness
5. fix deterministic report ordering, test classification, and project-scoped test-command behavior
6. improve implementation/query/connection/duplicate precision
7. split oversized test fixtures and add an isolated integration-test CI job
8. clear and reindex CodeMeridian, then rerun the dogfood acceptance matrix

## Completion Summary

All findings and definition-of-done items are complete. The implementation remains generic across indexed repositories; CodeMeridian-specific names occur only in self-hosted regression fixtures and audit evidence.

Final verification:

- .NET: 888 passed, 0 failed, including 57 isolated/live Neo4j integration tests
- Node/Vitest: 73 passed, 0 failed
- Node builds: all three workspaces passed
- NuGet vulnerability audit: no vulnerable direct or transitive packages
- npm audit: 0 vulnerabilities
- unified self-index: completed across C#, TypeScript, HTML/CSS, documentation, configuration, diagnostics, and keyword derivation
- drift verification: low, with the high failure threshold passing
- live graph dogfood: 25 direct test callers for `FindCoverageGapsAsync`, one direct MCP wrapper test for `QueryCodebaseAsync`, six direct duplicate-candidate tests, and a one-hop direct `Calls` connection from test to target
- session evaluation: `high` usefulness with 8 graph calls, 6 exact targets, no stale warnings, and no degraded context packs

The connected MCP process was running the pre-change server binary during this work. Relationship-level dogfood checks use the refreshed live graph; updated server-side routing, ranking, trust rendering, and precision behaviors are additionally covered by the passing Application, MCP, and Infrastructure suites and become live when the normal deployment restarts that process.

## Audit Baseline

### Graph state

- `check_graph_freshness(projectContext: "CodeMeridian")` sampled 20 high-confidence nodes and no medium/low-confidence nodes.
- `find_graph_drift(projectContext: "CodeMeridian")` reported low drift.
- This confidence applies to indexed node metadata, not to relationship completeness. The tool explicitly does not read source files, and the audit found missing or misresolved `Calls` relationships despite the high-confidence result.

### Tools exercised

The audit exercised representative tools across discovery, targeting, impact, testing, quality, configuration, and documentation:

- `check_graph_freshness`
- `find_graph_drift`
- `get_architectural_overview`
- `query_codebase`
- `resolve_exact_symbol`
- `get_context_for_editing`
- `build_minimal_context`
- `find_implementation_surface`
- `find_impact`
- `find_connection`
- `find_test_shield`
- `find_coverage_gaps`
- `find_hotspots`
- `find_large_nodes`
- `find_god_classes`
- `find_duplicate_candidates`
- `find_tool_dependency_impact`
- `find_config_definitions`
- `find_config_usage`
- `search_documentation`

Session evidence was recorded in `.meridian/sessions/2026-07-22-codex-self-tool-audit.jsonl`.

### Test baseline

All current tests passed:

- .NET: 846 passed, 0 failed, including 55 live Neo4j integration tests
- Node/Vitest: 73 passed, 0 failed
- focused Roslyn optional-parameter tests: 4 passed
- focused Application graph-tool tests: 26 passed
- focused MCP wrapper test: 1 passed

The green baseline does not cover incremental cross-file relationship preservation, score ordering after actionability partitioning, or the live dogfood scenarios that failed in the graph.

### Security baseline

- `dotnet list CodeMeridian.sln package --vulnerable --include-transitive` reports high-severity `Microsoft.OpenApi` 2.0.0 in `CodeMeridian.McpServer` and `CodeMeridian.McpServer.Tests`.
- `dotnet nuget why` traces it to `Microsoft.AspNetCore.OpenApi` 10.0.10.
- `npm audit --audit-level=high` reports high-severity `brace-expansion` 5.0.6.
- `npm explain brace-expansion` traces it through `minimatch` -> `@ts-morph/common` -> `ts-morph` in both TypeScript indexer workspaces.

### Maintainability baseline

Files materially above the repository size guidance include:

- `tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs`: 5,245 lines
- `tests/CodeMeridian.Infrastructure.Integration.Tests/Neo4jCodeGraphRepositoryIntegrationTests.cs`: 2,456 lines
- `src/Application/Services/CodebaseQueryService.Analytics.cs`: 1,611 lines
- `src/Application/Services/CodebaseQueryService.Analytics.Risk.cs`: 1,342 lines
- `src/Application/Services/CodebaseQueryService.Gds.cs`: 1,257 lines
- `src/Infrastructure/Graph/Neo4jCodeGraphRepository.Analytics.cs`: 1,092 lines
- `src/Application/Services/CodebaseQueryService.Surface.cs`: 1,037 lines

Production-file splitting should be tied to the behavioral fixes below rather than performed as an unrelated refactor. The 5,245-line test fixture should be split while adding the new regression suites.

## Findings And Required Changes

| ID | Priority | Finding | Classification | Status |
|---|---:|---|---|---|
| CM-AUDIT-001 | P0 | Incremental C# indexing can drop cross-file `Calls`, `Uses`, `Implements`, and related edges | confirmed correctness bug | Done |
| CM-AUDIT-002 | P1 | Freshness/drift reports high node confidence without measuring relationship completeness | confirmed trust-model gap | Done |
| CM-AUDIT-003 | P1 | Scored reports are reordered without their score, producing non-monotonic ranks | confirmed correctness bug | Done |
| CM-AUDIT-004 | P1 | TypeScript helpers under `tests/` can be classified and displayed as production | confirmed classification bug | Done |
| CM-AUDIT-005 | P1 | `FindTestShieldAsync` does not enter the resolved project-analysis option scope | confirmed configuration bug | Done |
| CM-AUDIT-006 | P1 | Two high-severity transitive dependency advisories are present and CI does not fail on them | confirmed security gap | Done |
| CM-AUDIT-007 | P2 | `find_implementation_surface` misses exact indexer/file-role targets for explicit goals | confirmed precision failure | Done |
| CM-AUDIT-008 | P2 | `query_codebase` advertises caller/dependency questions but currently performs semantic node search only | confirmed contract mismatch | Done |
| CM-AUDIT-009 | P2 | `find_duplicate_candidates` promotes extreme size/responsibility mismatches as low-risk extraction candidates | confirmed precision weakness | Done |
| CM-AUDIT-010 | P2 | `find_connection` can traverse unrelated siblings through undirected `Contains` edges | confirmed explanation-quality bug | Done |
| CM-AUDIT-011 | P3 | Coverage and large-node headings render `Classs` | confirmed formatting bug | Done |
| CM-AUDIT-012 | P2 | Integration coverage is excluded from normal CI and core analytics tests are concentrated in oversized fixtures | confirmed test-system gap | Done |
| CM-AUDIT-013 | P0 | Disabling an indexer can make its cached files look deleted and can erase unrelated graph surfaces | confirmed incremental-orchestration bug | Done |
| CM-AUDIT-014 | P1 | Documentation-only repositories are rejected as having no enabled indexer | confirmed orchestration bug | Done |
| CM-AUDIT-015 | P1 | Isolated CI integration tests assumed a pre-indexed graph, and natural-module GDS projection required relationship types absent from sparse fixtures | confirmed test-isolation and GDS projection bug | Done |
| CM-AUDIT-016 | P1 | Diagnostics cleanup deletes backward-compatible index-run metadata, leaving relationship completeness permanently unknown after a normal index | confirmed metadata-lifecycle bug | Done |

## Phase 0: Add Characterization And Regression Tests First — Done

### 0.1 Add an incremental C# relationship regression fixture

Primary files:

- `tools/RoslynIndexer/Pipeline/IndexerPipeline.cs`
- `tools/RoslynIndexer/Pipeline/CSharpIndexer.cs`
- `tools/RoslynIndexer/Pipeline/CSharpCallEdgeResolver.cs`
- new focused tests under `tests/CodeMeridian.RoslynIndexer.Tests/Pipeline/`

Create a two-file fixture:

- `Caller.cs` calls an optional-parameter method declared in `Callee.cs`
- the first pass indexes both files
- the second pass marks only `Caller.cs` as changed
- assert the second pass still emits a `Calls` edge from the caller to the unchanged callee

Add the inverse fixture:

- index both files
- change the callee while leaving the caller unchanged
- assert incoming call/reference edges from the unchanged caller are recreated against the new canonical target

Add deletion and rename cases:

- deleted callee must not leave an edge to a missing target
- renamed method/signature must not leave an edge to the old node id
- unchanged same-id callee must retain or recreate all incoming edges
- cross-file interface calls and `Implements`/`Uses` edges must follow the same rules

These tests should fail before CM-AUDIT-001 is fixed.

### 0.2 Add live tool-contract regression cases

Add an isolated graph fixture containing:

- one production method
- one direct test method
- one unrelated sibling test method
- one wrapper method
- deterministic `Calls` and `Contains` relationships

Verify:

- `find_test_shield` reports the direct test
- `find_coverage_gaps` excludes the directly tested method
- `find_connection` prefers the direct `Calls` path
- `get_context_for_editing` does not claim a tested method has no callers
- `build_minimal_context` and `find_test_shield` agree on the focused test set

Place application-format assertions in focused Application test classes and graph query semantics in Infrastructure integration tests.

## Phase 1: Resolve Dependency Advisories And Add Audit Gates — Done

### 1.1 Fix `Microsoft.OpenApi`

Primary files:

- `src/McpServer/CodeMeridian.McpServer.csproj`
- central package/version files if present
- `tests/CodeMeridian.McpServer.Tests/`
- `package-lock.json` only if the .NET change indirectly affects generated assets; otherwise do not touch it

Tasks:

- prefer upgrading `Microsoft.AspNetCore.OpenApi` to the smallest compatible 10.0.x version that resolves to patched `Microsoft.OpenApi`
- if the framework package does not yet carry a patched transitive dependency, add a deliberate direct `Microsoft.OpenApi` override at 2.7.5 or later on the compatible 2.x line
- verify Swagger/OpenAPI generation and endpoint behavior, not only restore success
- document the direct override if one is required so it can be removed after the parent package catches up

Acceptance criteria:

- `dotnet list CodeMeridian.sln package --vulnerable --include-transitive` reports no high/critical vulnerabilities
- MCP OpenAPI endpoint tests pass
- no package downgrade or binding warning is introduced

### 1.2 Fix `brace-expansion`

Primary files:

- root `package-lock.json`
- workspace lock files only if they are independently used by publishing workflows

Tasks:

- refresh the lock resolution to `brace-expansion` 5.0.7 or later without speculative major dependency upgrades
- use `npm audit fix` only after reviewing the exact lockfile diff
- keep `ts-morph`, `minimatch`, and optional native bindings compatible with Node 22 and the packaged indexer

Acceptance criteria:

- `npm audit --audit-level=high` exits successfully
- `npm test` passes all three workspaces
- `npm run build` passes all three workspaces

### 1.3 Make security audits blocking

Primary files:

- `.github/workflows/ci.yml`
- `.github/workflows/publish-indexer.yml`
- `.github/workflows/publish-mcp.yml`

Tasks:

- add a .NET high/critical transitive vulnerability gate
- add `npm audit --audit-level=high` after workspace restore
- ensure release workflows test/audit every Node workspace included in the published artifact, not only `TsIndexer`
- keep audit output visible as a distinct CI step

## Phase 2: Make Incremental C# Graph Updates Correct — Done

### 2.1 Separate scan scope from ingest scope

Current behavior filters the C# file list to changed files before extraction and resolution. Change the pipeline contract so it has two explicit sets:

- resolution/scan files: all current C# files needed to resolve cross-file symbols, constants, calls, and type references
- ingest files: changed files whose nodes and file-owned artifacts must be upserted

Recommended first implementation:

- parse all current C# files into an in-memory resolution catalog during an incremental C# pass
- resolve calls and type references against the full catalog
- ingest nodes only for changed files
- recreate resolved relationship edges from all surviving source files when a changed/deleted target can invalidate incoming edges
- avoid upserting unchanged nodes so `changeCount` is not artificially inflated
- keep deleted files absent from the catalog and delete their graph-owned data before relationship reconstruction

This is intentionally correctness-first. A later optimization may compute an affected-file closure, but it must not precede regression coverage.

### 2.2 Define edge ownership explicitly

Document and test whether each edge is owned by:

- its source file
- its target file
- both files
- a synthetic/configuration extractor

Use that ownership model when deleting changed files. A changed target must not permanently erase incoming edges owned by unchanged source files.

### 2.3 Preserve resolution diagnostics

Persist or emit deterministic run statistics:

- attempted call/reference edges
- resolved edges
- unresolved edges grouped by reason: missing target, ambiguous target, missing parameter metadata, missing receiver hint
- full versus incremental mode
- scanned file count versus ingested file count

Do not silently discard unresolved edges in `CSharpCallEdgeResolver.Resolve` or `CSharpReferenceEdgeResolver.Resolve` without a count and reason.

### 2.4 Verify watch mode and repeated incremental passes

Add tests for:

- two consecutive edits to the same caller
- caller edit followed by callee edit
- callee edit followed by caller edit
- delete and restore
- watch-mode batches with debouncing
- stable node/edge counts after an idempotent pass

### 2.5 Preserve disabled indexer state

Completed after self-hosting exposed CM-AUDIT-013:

- incremental cache plans retain out-of-scope entries while an indexer is disabled
- changes made while an indexer is disabled remain detectable when it is enabled again
- C# stale-file deletion accepts only C# paths and cannot remove documentation, TypeScript, frontend, or configuration graph data
- custom configuration-file patterns participate in both enumeration and cache-scope decisions
- documentation-only repositories are recognized as a valid enabled indexing surface
- focused cache, execution-context, file-selection, and C# path regressions cover the behavior

## Phase 3: Make Freshness Reflect Relationship Trust — Done

Primary files:

- graph/index metadata written by the indexer
- `src/Infrastructure/Graph/` freshness/drift queries
- `src/Application/Services/CodebaseQueryService.Surface.Freshness.cs`
- MCP freshness/drift tests and integration fixtures

### 3.1 Split confidence into node and relationship dimensions

Report separately:

- node metadata freshness
- source-hash confidence
- relationship completeness confidence
- last full index versus last incremental index

High node confidence must not imply that callers/callees are complete.

### 3.2 Detect suspicious edge drift

Use persisted index-run metadata to warn when:

- attempted edges exceed resolved edges
- resolved call/reference counts drop sharply after a small incremental batch
- changed files were indexed without a full resolution catalog
- a target has fresh node metadata but no expected cross-file relationships after a prior full-index baseline

### 3.3 Propagate uncertainty to dependent tools

When relationship confidence is unknown or low, these tools should render a warning instead of asserting safety:

- `get_context_for_editing`
- `find_impact`
- `find_test_shield`
- `find_coverage_gaps`
- `find_hotspots`
- `build_minimal_context`

In particular, never render `Callers — none (safe to change signature)` when relationship completeness is unknown.

### 3.4 Preserve relationship metadata during diagnostics refresh

The C# indexer emits index-run statistics as a `Diagnostic` node with `externalKind = IndexRun` so it remains compatible with older servers. The diagnostics phase runs after C# indexing and previously removed every `Diagnostic` node in the project, including this metadata.

Completed remediation:

- diagnostic cleanup excludes compatibility nodes whose `externalKind` is `IndexRun`
- ordinary compiler and linter diagnostics are still removed before refresh
- an isolated Neo4j integration regression proves both behaviors in the same project

## Phase 4: Fix Deterministic Analysis And Formatting — Done

### 4.1 Preserve metric ordering within actionability buckets

Primary file:

- `src/Application/Services/CodebaseQueryService.Ranking.cs`

Current generic partitioning orders by actionability metadata, change count, and line count, but ignores the supplied score. This caused `find_hotspots` ranks with fan-in values such as 46, 21, 44, 45, 28, and 57.

Replace the ambiguous generic ordering with an explicit comparer or key selector per tool:

- hotspots: fan-in descending
- PageRank: score descending
- high churn: change count descending
- downstream traversal: distance ascending

Keep actionability bucket selection first, then apply the tool's metric ordering inside each bucket, followed by deterministic file/name tie breakers.

Add tests that assert the exact row order, not only that rank labels and values exist.

### 4.2 Correct test-file classification

Primary files:

- `src/Application/Services/IndexedFileRoleOptions.cs`
- `meridian.json`
- `src/Application/Services/ConfiguredIndexedFileRoleClassifier.cs`
- `tests/CodeMeridian.Application.Tests/Services/FileRolePolicyTests.cs`

Add default patterns for helper files under test directories across supported languages:

- `tests/**/*.ts`, `tests/**/*.tsx`, `tests/**/*.js`, `tests/**/*.jsx`
- `test/**/*.ts`, `test/**/*.tsx`, `test/**/*.js`, `test/**/*.jsx`

Keep suffix patterns such as `*.test.ts` and `*.spec.ts`.

Add regression cases for `tests/walker-test-helpers.ts` and analogous C#/TSX helpers. Reindex after the configuration change and verify `find_hotspots` no longer places `TempProjectHarness.getRootPath` under production candidates.

### 4.3 Apply project-scoped analysis options in `FindTestShieldAsync`

Primary files:

- `src/Application/Services/CodebaseQueryService.Analytics.cs`
- `src/Application/Services/CodebaseQueryService.AnalysisScope.cs`
- focused `FindTestShield` tests

`FindTestShieldAsync` resolves the project context but does not execute inside `WithResolvedAnalysisOptionsAsync`. Wrap the complete analysis in the resolved option scope so project-specific ranking, file-role, and test-command strategies are honored.

Add tests for:

- project-specific `dotnet test` strategy produces a command for multiple direct tests in one directory
- TypeScript strategy produces a Vitest command
- concurrent requests for different projects do not leak options
- `find_test_shield` and `build_minimal_context` return the same command for the same test set

### 4.4 Fix pluralization

Replace the naive `$"{group.Key}s"` heading logic with deterministic labels such as `Classes` and `Methods`. Add snapshot/string assertions for mixed class/method coverage and large-node output.

## Phase 5: Improve Targeting And Explanation Precision — Done

### 5.1 Add acceptance scenarios for `find_implementation_surface`

Observed failures:

- an optional-parameter call-edge goal promoted `ICodeGraphRepository.cs` instead of `CSharpCallEdgeResolver.cs`
- an incremental cross-file resolution goal promoted unrelated query-service files
- a test-role classification goal promoted process-runner and document-indexer files instead of `IndexedFileRoleOptions.cs` and `ConfiguredIndexedFileRoleClassifier.cs`

Add deterministic fixture scenarios where the expected primary targets are known. Score exact goal/concept matches in symbol names, file names, and declaring types above broad goal-term density. Penalize contract/interface files when the goal explicitly names an implementation component and an exact implementation symbol exists.

Acceptance criteria:

- `CSharpCallEdgeResolver.cs`, `CSharpIndexer.cs`, and `IndexerPipeline.cs` are primary for the incremental call-edge scenario
- `IndexedFileRoleOptions.cs` and `ConfiguredIndexedFileRoleClassifier.cs` are primary for the test-role scenario
- unrelated MCP contracts and process runners are not primary
- confidence is not labeled `exact` merely because a candidate has canonical ids; exactness must also reflect goal-to-target evidence

### 5.2 Align `query_codebase` behavior with its tool contract

`query_codebase` currently passes the entire prompt into semantic node search. It does not deterministically implement advertised intents such as callers, callees, implementations, or dependencies.

Implement a small fact-oriented intent router for supported structural questions:

- callers of X
- callees/dependencies of X
- implementations of interface X
- members/types in namespace/module X

Resolve X to canonical ids first. If ambiguous, return candidates and request an exact target rather than mixing unrelated semantic results. Preserve semantic search as an explicit fallback for open-ended concept queries.

Add Application tests for routing and MCP contract tests for the examples in the tool description.

### 5.3 Prevent sibling-only `find_connection` paths from masquerading as behavior

Prefer `Calls`, `Uses`, `Implements`, `DependsOn`, endpoint, configuration, and data-flow relationships. Permit `Contains` only to enter or leave an enclosing type/file when necessary; do not traverse from one sibling method to another solely through their shared class or namespace.

If no semantic path exists, return no path or label the result as structural co-location instead of a behavioral connection.

Add regressions for:

- direct test call wins over a longer structural path
- unrelated methods in one test class are not presented as connected behavior
- namespace co-location is clearly labeled and lower confidence

### 5.4 Reduce duplicate-candidate false positives

Observed primary candidates included 223-line/16-line and 223-line/61-line method pairs with different responsibilities, despite 97%+ embedding similarity.

Add a configurable primary size-ratio requirement and use structural evidence before labeling a pair low-risk:

- same node type and layer
- bounded line-count ratio
- compatible parameter/return shape when available
- optional shared callees or normalized control-flow evidence

Pairs that only have embedding similarity should remain broader review candidates. Add tests for extreme size mismatch and same-file methods with different responsibilities.

## Phase 6: Improve Test Architecture And CI Coverage — Done

### 6.1 Split oversized test fixtures by behavior

Start with `CodebaseQueryServiceAnalyticsTests.cs`. Create one sealed test class per behavior family, for example:

- `CodebaseQueryServiceHotspotTests.cs`
- `CodebaseQueryServiceCoverageGapTests.cs`
- `CodebaseQueryServiceTestShieldTests.cs`
- `CodebaseQueryServiceMinimalContextTests.cs`
- `CodebaseQueryServiceImplementationSurfaceTests.cs`
- `CodebaseQueryServiceDuplicateCandidateTests.cs`
- `CodebaseQueryServiceFreshnessTests.cs`

Move shared builders into a small internal test factory in its own file. Do not weaken or rewrite assertions merely to facilitate the split.

Apply the same approach to `Neo4jCodeGraphRepositoryIntegrationTests.cs` after the new relationship/freshness fixtures are stable.

### 6.2 Add an isolated integration-test CI job

Normal CI currently excludes `CodeMeridian.Infrastructure.Integration.Tests`. Add a separate job with an isolated Neo4j/GDS service and bounded timeout.

Requirements:

- unique project contexts per test
- reliable cleanup
- no dependency on a developer or LAN Neo4j instance
- publish TRX logs on failure
- run at least on pull requests that touch `src/Infrastructure/Graph/`, indexers, or graph contracts; a full scheduled run is acceptable as an additional layer

### 6.3 Keep coverage behavior-focused

Do not respond to the current `find_coverage_gaps` list by adding tests blindly. Reindex after CM-AUDIT-001 and CM-AUDIT-004, then rerun coverage analysis. Prioritize the confirmed seams in this plan:

- incremental cross-file relationship preservation
- relationship freshness warnings
- per-tool metric ordering
- project-scoped `find_test_shield` options
- implementation-surface acceptance fixtures
- structural query intent routing
- connection path semantics
- duplicate size-ratio classification

## Phase 7: Reindex And Dogfood Acceptance — Done

After implementation:

1. run the full unit and integration suites
2. clear and reindex CodeMeridian using the repository-supported command
3. run drift verification
4. rerun the exact dogfood scenarios from this audit
5. compare graph/tool output to source facts
6. record a new session-evaluation file and run `codemeridian evaluate-session`

Suggested commands:

```powershell
dotnet test tests/CodeMeridian.RoslynIndexer.Tests/CodeMeridian.RoslynIndexer.Tests.csproj --filter FullyQualifiedName~Incremental
dotnet test tests/CodeMeridian.Application.Tests/CodeMeridian.Application.Tests.csproj --filter "FullyQualifiedName~Hotspot|FullyQualifiedName~CoverageGap|FullyQualifiedName~TestShield|FullyQualifiedName~ImplementationSurface"
dotnet test tests/CodeMeridian.Infrastructure.Integration.Tests/CodeMeridian.Infrastructure.Integration.Tests.csproj
dotnet test CodeMeridian.sln
npm run build
npm test
npm audit --audit-level=high
dotnet list CodeMeridian.sln package --vulnerable --include-transitive
dotnet run --project tools/Indexer -- . --project CodeMeridian --clear
codemeridian check-drift --project CodeMeridian --fail-on high
```

Dogfood acceptance matrix:

| Scenario | Expected result |
|---|---|
| `find_test_shield` on `FindCoverageGapsAsync` | direct Application tests are listed |
| `find_test_shield` on `QueryCodebaseAsync` | MCP wrapper forwarding test is listed |
| `build_minimal_context` on `FindDuplicateCandidatesAsync` | direct duplicate-candidate tests are listed |
| `find_coverage_gaps` | directly tested tool methods are not false-positive gaps |
| `find_hotspots` | production section excludes `tests/walker-test-helpers.ts` and fan-in is descending |
| `find_implementation_surface` for incremental call-edge bug | resolver/indexer/pipeline files are primary |
| `query_codebase` for callers of a canonical method | returns actual caller facts or exact ambiguity guidance |
| `find_connection` from direct test to target | returns the direct `Calls` edge, not sibling containment |
| `check_graph_freshness` after an intentionally degraded incremental fixture | relationship confidence warns even if node metadata is fresh |
| `find_duplicate_candidates` | extreme size mismatches are broader candidates, not low-risk primary candidates |

## Definition Of Done

- [x] Incremental C# passes preserve or correctly reconstruct cross-file relationships for caller edits, callee edits, deletes, and renames.
- [x] Unresolved relationship counts and reasons are observable.
- [x] Freshness distinguishes node freshness from relationship completeness.
- [x] Dependent tools warn when relationship completeness is unknown.
- [x] Diagnostics refresh preserves persisted index-run relationship statistics.
- [x] Hotspot, PageRank, churn, and distance-based reports preserve their documented metric order.
- [x] Test helpers under `tests/` are classified as tests across supported languages.
- [x] `FindTestShieldAsync` honors project-scoped analysis and test-command configuration without leakage.
- [x] Both high-severity dependency advisories are resolved and blocked by CI audits.
- [x] Implementation-surface acceptance fixtures return the exact indexer and file-role targets.
- [x] `query_codebase` behavior matches its documented structural-query examples.
- [x] `find_connection` does not present sibling-only containment paths as behavioral relationships.
- [x] Duplicate primary candidates meet semantic, structural, and size-ratio requirements.
- [x] `Classes`/`Methods` headings are grammatically correct.
- [x] Graph integration tests run in an isolated CI job.
- [x] Oversized analytics test fixtures are split into behavior-focused classes.
- [x] Disabled indexers preserve their cache and cannot delete another indexer's graph surface.
- [x] Documentation-only repositories run through the supported document indexer.
- [x] A clean reindex passes the dogfood acceptance matrix and the full .NET/Node/security validation commands.

## Deferred Work

The large-node and god-class outputs identified broader refactor candidates such as `CSharpAstWalker`, `IndexCommandHandler`, and the partial `Neo4jCodeGraphRepository`. Do not start broad extraction work as part of this remediation unless it is necessary for one of the confirmed fixes above. After graph correctness is restored, rerun `find_large_nodes`, `find_god_classes`, `find_impact`, and `find_test_shield` and create separate, narrowly scoped refactor plans for any remaining high-risk targets.
