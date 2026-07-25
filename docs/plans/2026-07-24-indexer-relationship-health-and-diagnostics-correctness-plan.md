# Indexer Relationship Health And Diagnostics Correctness Plan

Date: 2026-07-24

Status: implemented locally; remote MCP deployment acceptance pending

Scope: make C# relationship-resolution health metrics mathematically correct and actionable, distinguish expected external references from missing project-local edges, expose bounded evidence for remaining failures, and make diagnostic replacement/query/count behavior consistent across indexing, MCP tools, and `codemeridian doctor`.

## Outcome

After this plan:

- every attempted C# relationship candidate has exactly one recorded outcome
- duplicate edges and synthetic implementation edges cannot distort unresolved counts
- expected external/framework calls do not reduce project-local relationship confidence
- genuinely unresolved local and indeterminate relationships remain visible with deterministic samples
- `find_graph_drift`, freshness warnings, indexer logs, and `doctor` agree on relationship health
- a diagnostics pass replaces the previous project diagnostics while preserving index-run metadata
- `find_diagnostics` and `doctor` exclude index-run compatibility nodes from user diagnostic totals
- repeated force-full runs produce stable counts and preserve direct-test provenance

This plan does not require every framework or package method to become a graph node. External-call graph modeling is a separate feature.

## Current Evidence

### Operational baseline

- server reachable: yes
- MCP endpoint reachable: yes
- Neo4j reachable: yes
- indexed nodes: 6,451
- call edges: 3,894
- documentation chunks: 269
- graph drift: low
- sampled node metadata: High confidence
- full .NET suite: 913 passed
- direct diagnostics-cleanup test provenance: present in live `plan_edit_route`

### Relationship accounting defect

The latest full C# pass reported:

- attempted calls: 13,699
- resolved calls: 3,371
- attempted type references: 1,465
- resolved type references: 937
- displayed unresolved total: 10,856

The displayed total is `attempted - resolved`, but the operands do not describe the same population:

- attempts count raw extracted candidates
- resolved calls count distinct output edges after deduplication
- resolved type references include synthetic member-implementation edges

The resolver reason counts total 10,937, not 10,856. Duplicate calls create false unresolved arithmetic while synthetic edges offset real unresolved outcomes.

### Classification defect

Current reason counts combine expected unsupported/external references with project-local failures:

- `missing_target`: 9,093
- `unknown_member_receiver`: 897
- `ambiguous_target`: 521
- `missing_receiver_hint`: 426

`missing_target` usually means no candidate exists in the repository-local catalog. It does not prove a local edge is missing. The current warning nevertheless calls every item an unresolved local relationship.

### Diagnostic lifecycle inconsistency

The latest diagnostics pass printed one indexed diagnostic, while both `find_diagnostics` and `codemeridian doctor` reported 76. Index-run compatibility nodes explain only a small portion of the difference.

The cleanup integration test passes locally, so the live discrepancy must be reproduced and inspected before changing deletion semantics.

## Correctness Model

### Relationship outcome vocabulary

Use separate outcome counts for calls and type references:

| Outcome | Meaning | Reduces local confidence |
|---|---|---:|
| `resolved_local` | Candidate resolved to a repository-local node | No |
| `external_or_unindexed` | Available evidence points outside the local resolution catalog | No |
| `unresolved_local` | Receiver/type evidence points to a local symbol, but no safe target was selected | Yes |
| `indeterminate` | Syntax-only evidence cannot establish whether the target is local or external | Yes, but separately |
| `duplicate` | Candidate resolved, then collapsed into an already emitted edge | No |
| `synthetic` | Edge derived from resolved type/member structure rather than a raw attempt | No |

Required invariant for each edge kind:

```text
raw attempted
= resolved_local candidates
+ external_or_unindexed candidates
+ unresolved_local candidates
+ indeterminate candidates
```

`duplicate` is a property of resolved candidates, not an unresolved outcome. `synthetic` is reported outside raw attempt accounting.

### Trust policy

- High: full resolution catalog used and no unresolved-local or indeterminate candidates
- Medium: full catalog used, no known local failures, but indeterminate candidates remain
- Low: incomplete resolution catalog, known broad loss relative to the last full run, or unresolved-local failures above an explicit threshold
- Unknown: no compatible index-run metadata

Expected external targets must not lower confidence.

## Phase 0: Lock Reproductions And Schemas

### 0.1 Add accounting invariants

Primary files:

- `tools/RoslynIndexer/Pipeline/EdgeResolutionResult.cs`
- `tools/RoslynIndexer/Pipeline/IndexStats.cs`
- `tests/CodeMeridian.RoslynIndexer.Tests/Pipeline/CSharpCallEdgeResolverTests.cs`
- `tests/CodeMeridian.RoslynIndexer.Tests/Pipeline/CSharpReferenceEdgeResolverTests.cs`
- `tests/CodeMeridian.RoslynIndexer.Tests/Pipeline/CSharpIncrementalIndexerTests.cs`

Add failing tests for:

- two identical call sites that resolve to one persisted edge
- one raw type relationship that also generates member implementation edges
- mixed resolved, external, local-failure, and indeterminate candidates
- the accounting invariant for calls and references independently
- deterministic counts across repeated full and incremental resolution passes

Do not change trust thresholds until these tests prove the raw metrics.

### 0.2 Reproduce live diagnostic replacement

Primary files:

- `tools/Indexer/Cli/DiagnosticsCommand.cs`
- `src/Sdk/CodeMeridianClient.cs`
- `src/McpServer/Api/KnowledgeApiEndpoints.cs`
- `src/Infrastructure/Graph/Neo4jCodeGraphRepository.cs`
- `tests/CodeMeridian.Infrastructure.Integration.Tests/Neo4jCodeGraphRepositoryDeleteDiagnosticsIntegrationTests.cs`

Add an isolated project fixture containing:

- two ordinary diagnostics from a previous run
- one compatibility `IndexRun` node stored as `Diagnostic`
- one native `IndexRun` node when supported

Run the same clear-and-ingest path used by the CLI, then assert:

- old ordinary diagnostic IDs are gone
- new ordinary diagnostic IDs are present
- both compatible and native index-run metadata survive as intended
- project normalization cannot make cleanup miss the fixture
- cleanup is project-scoped

Against the live server, capture:

- server version
- deleted ordinary diagnostic count
- ordinary diagnostic count before and after replacement
- index-run count before and after replacement

If the integration path passes but live cleanup fails, treat server deployment/version skew as the root cause before changing Cypher.

### 0.3 Define compatible index-run metadata v2

Persist a schema/version marker and separate fields for:

- raw attempted candidates by edge kind
- resolved-local candidates by edge kind
- unique emitted edges by edge kind
- duplicate resolved candidates
- synthetic edges
- external-or-unindexed candidates
- unresolved-local candidates
- indeterminate candidates
- bounded reason counts and samples
- full versus partial resolution catalog

Continue writing the existing aggregate fields for one compatibility window. Readers must prefer v2 fields and safely fall back to legacy metadata.

## Phase 1: Make Resolution Accounting Exact

### 1.1 Record one outcome per raw candidate

Primary files:

- `tools/RoslynIndexer/Pipeline/EdgeResolutionResult.cs`
- `tools/RoslynIndexer/Pipeline/CSharpCallEdgeResolver.cs`
- `tools/RoslynIndexer/Pipeline/CSharpReferenceEdgeResolver.cs`

Replace the loose attempted/resolved counters with an outcome collector keyed by edge kind and disposition.

Requirements:

- record the outcome before output-edge deduplication
- count a successfully selected duplicate as `resolved_local`
- report duplicate collapse separately
- report generated member implementation edges as `synthetic`
- keep reason counts nested under their disposition and edge kind
- assert the accounting invariant in tests and optionally in debug builds

### 1.2 Keep persisted-edge counts separate

Primary files:

- `tools/RoslynIndexer/Pipeline/CSharpIndexer.cs`
- `tools/RoslynIndexer/Pipeline/IndexStats.cs`

Expose distinct concepts:

- candidate resolution quality
- unique graph edges emitted
- total edges ingested

Do not use persisted-edge counts to infer candidate failures.

Update indexer logs to show a compact summary such as:

```text
Calls: 13,699 candidates; 3,477 resolved locally; 8,900 external/unindexed;
897 indeterminate; 425 unresolved local; 106 duplicate edges collapsed.
```

Numbers above are illustrative, not acceptance targets.

## Phase 2: Classify External, Local, And Indeterminate Targets

### 2.1 Classify call outcomes conservatively

Primary files:

- `tools/RoslynIndexer/Pipeline/CSharpAstWalker.cs`
- `tools/RoslynIndexer/Pipeline/CSharpCallEdgeResolver.cs`

Use existing receiver evidence:

- receiver kind
- receiver type hint
- declaring type
- namespace
- test-subject convention
- method name and compatible arity
- presence of the receiver type in the full local type catalog

Classification rules:

- known receiver type absent from the local catalog: `external_or_unindexed`
- known local receiver type with no compatible member: `unresolved_local`
- unknown member receiver with no safe local convention: `indeterminate`
- unqualified call with no repository-local candidate: `external_or_unindexed` unless local-function evidence says otherwise
- ambiguous candidates after local receiver/namespace narrowing: `unresolved_local`
- never manufacture a local edge solely from name/arity uniqueness for an unknown receiver

Preserve the recently added inherited test-fixture convention and its negative controls.

### 2.2 Classify type-reference outcomes

Primary files:

- `tools/RoslynIndexer/Pipeline/CSharpReferenceEdgeResolver.cs`
- `tools/RoslynIndexer/Pipeline/CSharpAstWalker.cs`

Rules:

- exact local type ID or a unique safe local candidate: `resolved_local`
- type absent from the full local catalog: `external_or_unindexed`
- multiple repository-local candidates without safe namespace/file evidence: `unresolved_local`
- missing extraction metadata: `indeterminate`

Keep `Implements`, `Inherits`, and ordinary `Uses` reason counts separate.

### 2.3 Store bounded deterministic samples

For each unresolved-local and indeterminate reason, persist at most a small fixed number of samples containing:

- source node ID
- source file and line when available
- edge kind
- target/call name
- argument count when relevant
- receiver kind and type hint
- reason

Sort samples deterministically. Do not store source bodies, argument values, secrets, or unbounded payloads.

Expose samples in indexer logs and drift output so remaining resolver bugs can be reproduced without instrumenting production.

## Phase 3: Fix Relationship Trust And User Guidance

### 3.1 Parse v2 metadata with legacy fallback

Primary files:

- `src/Application/Services/CodebaseQueryService.RelationshipTrust.cs`
- `tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceRelationshipTrustTests.cs`

Requirements:

- prefer v2 disposition fields
- calculate confidence from unresolved-local and indeterminate counts
- ignore expected external targets for confidence
- never calculate unresolved candidates as `attempted - persisted edges`
- label legacy arithmetic explicitly as an estimate
- keep full/incremental catalog warnings

### 3.2 Render actionable drift evidence

Primary files:

- `src/Application/Services/CodebaseQueryService.Surface.Freshness.cs`
- `tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceFindGraphDriftTests.cs`

Render separate sections for:

- known local resolution failures
- indeterminate syntax-only relationships
- expected external/unindexed targets
- deduplicated and synthetic edge counts
- deterministic failure samples

Recommendations must match the failing dimension:

- no full catalog: run a force-full/non-destructive relationship index
- unresolved local samples: inspect the named resolver cases
- only external targets: no remediation
- only indeterminate cases: explain the syntax-only limitation
- node metadata drift: use the existing node-specific guidance

### 3.3 Align CLI status and verification

Primary files:

- `tools/Indexer/Cli/StatusCommand.cs`
- drift/check command implementation under `tools/Indexer/Cli/`
- related CLI tests

`doctor`, `check-drift`, freshness tools, and indexer logs must report the same relationship disposition totals and confidence.

Add a verification failure threshold based on unresolved-local relationships, not total external calls.

## Phase 4: Make Diagnostic Replacement And Counts Consistent

### 4.1 Exclude index-run metadata from user diagnostics

Primary files:

- `src/Infrastructure/Graph/Neo4jCodeGraphRepository.cs`
- `tests/CodeMeridian.Infrastructure.Integration.Tests/Neo4jCodeGraphRepositoryFindDiagnosticsIntegrationTests.cs`
- `tests/CodeMeridian.Infrastructure.Integration.Tests/Neo4jCodeGraphRepositoryFindDiagnosticsForNodeIntegrationTests.cs`
- `tests/CodeMeridian.Infrastructure.Integration.Tests/Neo4jCodeGraphRepositoryCountMethodsIntegrationTests.cs`

Update ordinary diagnostic queries and counts to exclude compatibility nodes where `externalKind = IndexRun`.

Relationship-trust lookup remains responsible for reading compatible and native index-run nodes.

### 4.2 Verify cleanup rather than trusting HTTP success

Primary files:

- `src/Core/CodeGraph/ICodeGraphRepository.cs`
- `src/Infrastructure/Graph/Neo4jCodeGraphRepository.cs`
- `src/McpServer/Api/KnowledgeApiEndpoints.cs`
- `src/Sdk/CodeMeridianClient.cs`
- `tools/Indexer/Cli/DiagnosticsCommand.cs`

Prefer a cleanup response that reports the deleted ordinary-diagnostic count. After replacement, verify the persisted ordinary count matches the distinct findings submitted.

Keep the change surgical:

- preserve index-run nodes
- do not clear configuration, code, or documents
- do not broaden project scope
- fail the diagnostics phase clearly if cleanup or replacement is incomplete

If atomic replacement can be added without crossing architecture boundaries, use one backend operation. Otherwise document the clear-then-ingest failure mode and make postcondition verification mandatory.

### 4.3 Add CLI lifecycle tests

Primary files:

- `tests/CodeMeridian.Indexer.Tests/Cli/DiagnosticsCommandTests.cs`
- `tests/CodeMeridian.Indexer.Tests/Cli/DiagnosticsCommandCoverageTests.cs`
- SDK and MCP endpoint tests

Cover:

- zero new diagnostics removes all prior ordinary diagnostics
- one new diagnostic replaces many old diagnostics
- repeated identical runs remain idempotent
- a cleanup failure stops ingestion
- an ingestion failure is surfaced
- compatibility index-run metadata survives
- `doctor` and `find_diagnostics` return the same ordinary diagnostic total

Avoid reflection-only coverage for the lifecycle seam; introduce a narrow testable collaborator if necessary.

## Phase 5: Documentation And Compatibility

Update:

- `docs/indexing.md`
- `tools/RoslynIndexer/supports.md`
- `tools/Indexer/README.md`

Document:

- best-effort local call resolution
- external/unindexed versus unresolved-local outcomes
- relationship-confidence rules
- deterministic unresolved samples
- force-full versus clear guidance
- diagnostic replacement semantics
- index-run metadata exclusion from ordinary diagnostics
- compatibility behavior for legacy index-run metadata

Do not promise complete framework call graphs.

## Verification Strategy

### Focused tests

Run:

```powershell
dotnet test tests/CodeMeridian.RoslynIndexer.Tests/CodeMeridian.RoslynIndexer.Tests.csproj --no-restore
dotnet test tests/CodeMeridian.Application.Tests/CodeMeridian.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~RelationshipTrust|FullyQualifiedName~FindGraphDrift"
dotnet test tests/CodeMeridian.Indexer.Tests/CodeMeridian.Indexer.Tests.csproj --no-restore --filter "FullyQualifiedName~DiagnosticsCommand"
dotnet test tests/CodeMeridian.Infrastructure.Integration.Tests/CodeMeridian.Infrastructure.Integration.Tests.csproj --no-restore --filter "FullyQualifiedName~Diagnostic"
```

### Full regressions

Run:

```powershell
dotnet test --no-restore
npm test
```

### Live acceptance

With the updated server and indexer:

```powershell
dotnet run --project tools/Indexer -- . --force-full
dotnet run --project tools/Indexer -- doctor --project CodeMeridian
dotnet run --project tools/Indexer -- check-drift --project CodeMeridian
```

Then verify:

- two consecutive force-full runs produce identical disposition and unique-edge counts
- accounting invariants hold for calls and type references
- drift does not call external targets unresolved local relationships
- unresolved-local and indeterminate samples are bounded and reproducible
- direct-test provenance for `DeleteDiagnosticsAsync` remains present
- production hotspots contain no test-path nodes
- a diagnostics run that indexes one ordinary diagnostic yields one ordinary diagnostic in both `find_diagnostics` and `doctor`
- old diagnostic IDs are absent
- index-run relationship metadata remains available

## Acceptance Criteria

- [x] Raw attempted counts equal the sum of mutually exclusive candidate outcomes for each edge kind.
- [x] Duplicate collapse and synthetic edges are reported separately.
- [x] External/unindexed targets do not lower relationship confidence.
- [x] Known local failures and indeterminate cases remain visible separately.
- [x] Indexer logs, persisted metadata, freshness, drift, `doctor`, and `check-drift` use the same v2 reader locally.
- [x] Legacy index-run metadata remains readable and is labeled as estimated.
- [x] Ordinary diagnostic queries and counts exclude compatibility index-run nodes.
- [x] Diagnostic replacement removes old ordinary diagnostics and preserves index-run metadata in integration coverage.
- [x] Repeated force-full relationship runs are deterministic and diagnostic lifecycle paths are idempotent in tests.
- [x] Direct-test provenance and stale-role protections continue to pass live graph acceptance.
- [x] Focused, full .NET, and TypeScript test suites pass.

## Implementation Evidence

Implemented on 2026-07-24 for both Roslyn and TypeScript:

- Roslyn and TypeScript emit schema-v2, scope-aware relationship outcomes with exact accounting, unique-edge totals, duplicate counts, synthetic counts, bounded samples, and full/partial-catalog evidence.
- Imported TypeScript aliases are classified from their aliased declarations, preventing an import declaration in the current file from falsely making an external package project-local.
- TypeScript compiler/lint diagnostics are owned only by the unified diagnostics phase, so `--skip-diagnostics` is honored and replacement runs once per project.
- The diagnostics delete endpoint returns its deleted ordinary count; the CLI verifies the final ordinary count through project status.
- Ordinary diagnostic count/find/nearby queries exclude compatibility `IndexRun` nodes.

Verification:

- full .NET solution: 924 passed
- JavaScript/TypeScript workspaces: 75 passed
- focused Neo4j diagnostic integration tests: 3 passed
- two live C# force-full passes produced identical relationship disposition, duplicate, synthetic, node, and edge counts
- a repeated live TypeScript pass produced identical disposition and edge counts
- live `plan_edit_route` retained `DeleteDiagnosticsAsync_PreservesCompatibleIndexRunMetadata` as the test step
- live production hotspots contained no test-path nodes

Deployment note: the configured remote MCP server at `192.168.10.70:5100` still reports the pre-change source revision. It accepted and stored v2 index-run nodes, but its old reader continues to display `attempted - persisted edges` as 11,159 unresolved local relationships and its old diagnostic query reports 80 items, including four compatibility index-run nodes. Redeploy the MCP server from this source state before running the final `doctor`, `check-drift`, and diagnostic replacement acceptance commands.

## Suggested Implementation Order

1. Phase 0 accounting and diagnostic lifecycle regressions.
2. Phase 1 exact outcome accounting.
3. Phase 2 conservative classification and samples.
4. Phase 3 trust/reporting consumers.
5. Phase 4 diagnostic lifecycle correction.
6. Phase 5 documentation and compatibility notes.
7. Full reindex, repeated-run comparison, and live acceptance.

Do not tune confidence thresholds or suppress warnings before exact accounting and classification are in place.
