# Indexer Relationship Completeness Remediation Plan

- Date: 2026-08-05
- Status: implementation in progress; first safe C#/TypeScript/health-evidence slices complete and locally verified, live re-index acceptance pending
- Scope: reduce false and avoidable C# and TypeScript relationship-resolution failures, make incremental catalogs trustworthy, and calibrate the relationship-confidence policy from measured precision
- Primary components: Roslyn indexer, TypeScript indexer, index orchestration, relationship-trust reader, CLI/MCP diagnostics
- Related completed plan: [Indexer Relationship Health And Diagnostics Correctness Plan](2026-07-24-indexer-relationship-health-and-diagnostics-correctness-plan.md)

## Executive Answer

There is not one accounting or database-corruption bug behind the `Low` warning. The schema-v2 accounting invariant is working: every raw relationship candidate is classified exactly once, and external/unindexed, duplicate, and synthetic outcomes are reported separately.

There are, however, several actionable indexer limitations and likely false classifications:

1. The C# walker parses syntax trees without a Roslyn `Compilation` or `SemanticModel`. Its receiver inference handles only a small set of syntax shapes. This produces 6,763 current `unknown_member_receiver` outcomes.
2. The C# resolver does not fully model same-declaring-type calls, `params`, extension-method receiver adjustment, inherited members, or external base members. These limitations contribute to 870 current unresolved-local outcomes.
3. TypeScript incremental batches intentionally use only changed files as their node and resolution catalog. A changed file can therefore see a local declaration through ts-morph while that declaration is absent from the batch catalog, producing `local_declaration_not_indexed` and a partial-catalog warning.
4. The trust policy is deliberately conservative: one unresolved-local relationship makes the whole project `Low`, and any partial catalog also makes it `Low`. Changing that threshold alone would hide evidence rather than improve the graph.

The remediation must improve relationship evidence and catalog completeness first. Trust thresholds may be calibrated only after precision and recall are measured on deterministic fixtures and live full/incremental runs.

## Current Live Baseline

Captured from schema-v2 `IndexRun` nodes on 2026-08-05. The current run for each `(language, resolutionScope)` is shown because that is what `CodebaseQueryService.RelationshipTrust` evaluates.

| Language / scope | Mode | Full catalog | Attempted | Resolved local | External/unindexed | Unresolved local | Indeterminate |
|---|---|---:|---:|---:|---:|---:|---:|
| C# / project | incremental | Yes | 16,665 | 4,486 | 4,546 | 870 | 6,763 |
| TypeScript / Evolution Web | incremental | No | 171 | 12 | 43 | 115 | 1 |
| TypeScript / HtmlCssIndexer | incremental | No | 27 | 0 | 27 | 0 | 0 |
| TypeScript / IndexerShared | full | Yes | 205 | 60 | 134 | 10 | 1 |
| TypeScript / TsIndexer | incremental | No | 27 | 0 | 27 | 0 | 0 |
| **Total used by trust reader** |  |  | **17,095** | **4,558** | **4,777** | **995** | **6,765** |

Important interpretation:

- `external/unindexed` does not lower confidence and is not a remediation target by itself.
- duplicate candidates and synthetic edges are healthy accounting dimensions, not missing relationships.
- the actionable total is 995 unresolved-local plus 6,765 indeterminate candidates.
- 6,763 of 6,765 indeterminate candidates are C# `unknown_member_receiver` outcomes.
- 115 TypeScript unresolved-local candidates come from the partial Evolution Web incremental catalog.
- the latest C# incremental run used a full relationship catalog, so a full re-index alone will not solve the dominant C# issue.

### Current C# reason distribution

| Disposition / reason | Count | Initial interpretation |
|---|---:|---|
| `indeterminate:unknown_member_receiver` | 6,763 | Receiver expression shape or type could not be inferred safely. Dominant issue. |
| `unresolved_local:missing_receiver_hint` | 456 | Unqualified call has multiple plausible local candidates; declaring-type evidence is not ranked strongly enough. |
| `unresolved_local:local_target_incompatible_arity` | 311 | Candidate exists but current arity rules reject it; inspect `params`, extension methods, optional/generic overloads, and extraction errors. |
| `unresolved_local:local_target_missing` | 52 | Receiver is believed local but the named member is absent from the catalog; inherited/external-base cases may be false local failures. |
| `unresolved_local:ambiguous_local_target` | 50 | Typed receiver still maps to multiple candidates; namespace/type identity needs stronger normalization. |
| `unresolved_local:ambiguous_local_type` | 1 | Type reference needs namespace/alias disambiguation. |

### Representative live evidence

- `string.IsNullOrWhiteSpace(...)` can be classified as an unknown member because predefined-type receiver syntax is not handled like an identifier.
- fluent service-registration chains such as `services.Add...(...sp => sp.GetRequiredService(...))` lose the receiver type across invocation and lambda boundaries.
- unqualified helpers such as `Normalize(...)` can remain ambiguous even when the source declaring type contains the intended target.
- inherited framework helpers such as `CreateClient()` on a local test factory can be marked local-target-missing even though the member is supplied by an external base class.
- TypeScript incremental batches construct `knownIds` and the method index only from changed files because `skipAddingFilesFromTsConfig` and `skipFileDependencyResolution` are enabled.

## Success Definition

The goal is not a zero-warning graph obtained by reclassification. The goal is a graph where each disposition is supported by reproducible evidence and useful local edges are recovered without adding false edges.

### Required outcomes

- [x] Preserve the schema-v2 accounting invariant for every language, scope, and edge kind.
- [x] Preserve deterministic unique-edge, duplicate, synthetic, and external/unindexed counts.
- [ ] Reduce C# `unknown_member_receiver` materially on the CodeMeridian fixture without unsafe name-only fallback.
- [ ] Resolve common same-type, lambda/local-variable, chained-receiver, `params`, extension, and inherited-member cases when evidence is sufficient.
- [x] Make TypeScript incremental runs use a complete resolution catalog while emitting and deleting only changed-file graph data.
- [x] Keep external/framework calls from becoming noisy local graph edges.
- [ ] Make full and incremental relationship results comparable by scope and reason.
- [ ] Calibrate High/Medium/Low only after precision gates pass.
- [ ] Keep all failure samples bounded and free of source bodies, argument values, credentials, and arbitrary graph properties.

### Initial quantitative targets

These are investigation gates, not promises to manufacture edges:

- [ ] Reduce C# indeterminate rate from 40.6% of C# candidates to below 20% in the first safe slice.
- [ ] Reduce C# unresolved-local rate from 5.2% to below 3% without reducing fixture precision.
- [ ] Eliminate partial-catalog warnings for normal TypeScript incremental indexing.
- [ ] Eliminate `local_declaration_not_indexed` caused solely by unchanged files being absent from the incremental catalog.
- [ ] Achieve 100% precision on the curated positive/negative receiver-resolution fixture.
- [ ] Keep two consecutive full runs and two identical incremental replays deterministic.

Do not lower the trust warning merely to meet these numbers.

## Implementation Evidence: 2026-08-05

Completed in the first safe implementation slices:

- [x] Extracted invocation/receiver evidence from `CSharpAstWalker` into `CSharpInvocationEvidenceExtractor` with a small typed evidence record.
- [x] Added syntax evidence for predefined receivers, explicit lambda/anonymous-method parameters, `this` member access, `foreach`, `catch`, declaration patterns, casts, parentheses, null-forgiving access, conditional access, and object creation.
- [x] Added exact same-declaring-type and indexed local base/interface candidate selection with a negative control against unrelated unique methods.
- [x] Added `params`, extension receiver, and explicit generic-arity metadata and compatibility rules.
- [x] Reclassified a possible member supplied by an unindexed external base as indeterminate instead of a known local failure.
- [x] Separated TypeScript emission files from the full project-root resolution catalog. Incremental runs emit only changed-file nodes but resolve edges to unchanged local targets.
- [x] Persisted `usedFullResolutionCatalog=true` for the TypeScript incremental path only after full project-root discovery is used.
- [x] Added bounded top call/reference reason counts and invariant-culture percentages to relationship trust/freshness explanations.
- [x] Documented the new C# and TypeScript guarantees in `docs/indexing.md`, `tools/RoslynIndexer/supports.md`, and `tools/Indexer/README.md`.

Local verification:

- [x] `dotnet test CodeMeridian.sln --no-restore`: 997 passed, 6 skipped live-acceptance tests, 0 failed.
- [x] `npm test` in `tools/TsIndexer`: 68 passed, 0 failed.
- [x] `npm test --prefix tools/HtmlCssIndexer`: 7 passed, 0 failed.
- [x] `npm run build` in `tools/TsIndexer`: passed and refreshed tracked `dist` assets.
- [x] `git diff --check`: no whitespace errors.

Still intentionally pending:

- [ ] Deploy/restart the updated indexer and server, run a non-destructive force-full index, and capture after metrics.
- [ ] Run two full and two fixed incremental replays to prove live determinism.
- [ ] Measure whether the C# indeterminate/unresolved quantitative targets are met.
- [ ] Benchmark TypeScript full-catalog incremental latency and memory per root.
- [ ] Decide the semantic-enrichment gate for fluent return-value chains; syntax-only indexing remains the shipped behavior for now.
- [ ] Calibrate trust thresholds only after the live precision review; the unresolved-local threshold remains unchanged at `1`.

## Non-Goals

- [ ] Do not create graph nodes for every framework or NuGet/npm member.
- [ ] Do not resolve unknown receivers by globally unique method name alone.
- [ ] Do not treat external/unindexed outcomes as defects without local evidence.
- [ ] Do not run `--clear` for ordinary relationship refreshes.
- [ ] Do not require a successful build for basic syntax indexing; semantic enrichment must degrade safely.
- [ ] Do not load or persist source bodies as diagnostic evidence.
- [ ] Do not combine C#, TypeScript, HTML/CSS, and external-call modeling into one universal resolver.

## Likely Edit Surface

### C# extraction and resolution

- `tools/RoslynIndexer/Pipeline/CSharpAstWalker.cs`
- new focused receiver-evidence collaborator under `tools/RoslynIndexer/Pipeline/`
- `tools/RoslynIndexer/Pipeline/CSharpCallEdgeResolver.cs`
- `tools/RoslynIndexer/Pipeline/CSharpReferenceEdgeResolver.cs`
- `tools/RoslynIndexer/Pipeline/CSharpIndexer.cs`
- `tools/RoslynIndexer/Pipeline/EdgeResolutionResult.cs`

### TypeScript catalog and orchestration

- `tools/Indexer/Cli/IndexCommandHandler.cs`
- `tools/Indexer/Cli/TypeScriptIndexerCommandBuilder.cs`
- `tools/TsIndexer/src/application/type-script-indexer-application.ts`
- `tools/TsIndexer/src/walker.ts`
- `tools/TsIndexer/src/walker/graph.ts`
- `tools/TsIndexer/src/relationship-health.ts`

### Trust, diagnostics, and documentation

- `src/Application/Services/CodebaseQueryService.RelationshipTrust.cs`
- `src/Application/Services/CodebaseQueryService.Surface.Freshness.cs`
- `tools/Indexer/Cli/StatusCommand.cs`
- `docs/indexing.md`
- `tools/RoslynIndexer/supports.md`
- `tools/Indexer/README.md`

## Phase 0: Lock A Reproducible Baseline

### 0.1 Export current outcome facts

- [ ] Add or reuse a bounded CLI/GraphQL report that prints one row per language/scope/mode.
- [ ] Include attempted, resolved-local, external/unindexed, unresolved-local, indeterminate, duplicate, synthetic, scanned-file, and catalog-completeness fields.
- [ ] Include reason histograms separately for calls and type references.
- [ ] Include percentages as well as counts so repository growth does not look like a regression by itself.
- [ ] Include production/test file-role breakdowns for unresolved and indeterminate outcomes.
- [ ] Never print API keys, source bodies, argument text, or arbitrary property dictionaries.

### 0.2 Create a deterministic relationship fixture corpus

Add small checked-in fixtures or inline source cases covering:

- [ ] predefined static receivers: `string`, `int`, `DateTime`, and aliases
- [ ] typed parameters, fields, properties, locals, `var`, object creation, casts, and parenthesized receivers
- [ ] conditional access and null-forgiving receivers
- [ ] lambda parameters, local functions, `foreach`, `catch`, `using`, pattern variables, and deconstruction
- [ ] fluent/chained calls and calls on method-return values
- [ ] same-type unqualified calls and overloaded helpers
- [ ] optional parameters, `params`, generic methods, and extension methods
- [ ] local inheritance/interface members and external-base members
- [ ] ambiguous same-name candidates across namespaces/files
- [ ] negative controls where no safe local edge exists

For every case, assert both the selected edge and the disposition/reason when no edge is selected.

### 0.3 Capture before metrics

- [ ] Run a non-destructive force-full index.
- [ ] Capture outcome totals and reason histograms.
- [ ] Run the same force-full index again and require identical relationship totals.
- [ ] Replay a fixed incremental change set twice and require identical totals.
- [ ] Store the command, indexer version, commit, timestamp, and normalized results in implementation evidence.

## Phase 1: Improve C# Receiver Evidence

### 1.1 Extract receiver analysis from the large walker

`CSharpAstWalker.cs` is already well above the preferred file size. Keep the change reviewable:

- [x] Extract invocation/receiver evidence into one focused internal collaborator.
- [x] Keep syntax walking and node ownership in `CSharpAstWalker`.
- [ ] Return a small typed evidence record: call name, supplied argument count, receiver kind, receiver type hint, declaring-type hint, generic arity, and confidence/source.
- [x] Keep one public type per file and avoid a universal relationship framework.
- [x] Preserve current edge-property names for compatibility; add fields only when required.

### 1.2 Cover missing syntax-only receiver shapes

- [x] Recognize predefined static receiver syntax such as `string.IsNullOrWhiteSpace`.
- [ ] Unwrap parentheses, null-forgiving expressions, casts, `await`, conditional access, and object creation.
- [x] Track explicitly typed local variables and improve safe `var` inference from object creation and casts. Known-local-call return inference remains part of the semantic-enrichment gate.
- [ ] Track lambda, anonymous-method, `foreach`, `catch`, `using`, pattern, and deconstruction variables when their types are explicit or safely inferable.
- [x] Preserve receiver evidence through member-access chains when an intermediate current-type member type is known.
- [ ] Do not infer a local type from capitalization alone when aliases or namespaces make the result ambiguous.

### 1.3 Semantic enrichment decision gate

Syntax-only improvements will not safely resolve all fluent chains or return-value types. Run a bounded spike before adding a compilation dependency:

- [ ] Evaluate `MSBuildWorkspace`/Roslyn `Compilation` loading across solution, project, generated-code, and partially broken-build cases.
- [ ] Measure startup time, memory, and indexing latency on CodeMeridian.
- [ ] Verify semantic resolution works when NuGet assets are present and degrades to syntax-only evidence when unavailable.
- [ ] Compare recovered-edge precision against the fixture corpus and live samples.
- [ ] Adopt semantic enrichment only if it remains bounded and does not make basic indexing depend on a clean build.
- [ ] If adopted, record evidence source (`semantic`, `syntax`, or `heuristic`) in bounded relationship metadata.

Stop condition: do not ship semantic enrichment if failed project loading makes indexing less reliable than the current syntax-only path.

## Phase 2: Improve C# Candidate Selection

### 2.1 Rank same-declaring-type calls safely

- [x] For unqualified calls, prefer a compatible candidate on the source declaring type before file/namespace heuristics.
- [x] Include local base/interface members when inheritance evidence is exact.
- [x] Preserve ambiguity when multiple same-type overloads remain compatible.
- [x] Add negative controls proving unrelated globally unique methods are not selected.

### 2.2 Model callable arity correctly

- [x] Persist whether a method has a `params` parameter.
- [x] Treat `params` as an unbounded compatible upper arity after required parameters.
- [x] Preserve optional-parameter lower bounds.
- [x] Account for the implicit receiver parameter on extension methods.
- [x] Record generic arity separately from value-parameter arity.
- [ ] Distinguish true incompatible arity from insufficient extraction metadata.

### 2.3 Handle inherited and external-base members

- [x] Build a bounded local type hierarchy catalog from exact `Inherits`/`Implements` evidence.
- [x] Resolve members declared on indexed local bases/interfaces.
- [x] When a local receiver inherits an external base and no local member exists, classify conservatively as external/unindexed or indeterminate rather than automatically unresolved-local.
- [ ] Keep a local failure when exact local interface/base evidence promises the missing member.
- [ ] Add `CreateClient()`-style framework inheritance fixtures and local-inheritance negative controls.

### 2.4 Normalize type identity

- [ ] Compare canonical namespace-qualified type identity when available.
- [ ] Normalize generic type display, nullable suffixes, aliases, arrays, and nested types.
- [ ] Use imports/usings and namespace evidence to resolve the one ambiguous local type case.
- [ ] Avoid merging distinct same-short-name types.

## Phase 3: Give TypeScript Incremental Runs A Full Resolution Catalog

### 3.1 Separate emission files from resolution files

Mirror the successful C# concept of changed files versus resolution files:

- [x] Pass the changed-file batch as the emission-file set.
- [x] Load all current project-root TypeScript source files needed for the resolution catalog and symbol resolution.
- [x] Build `knownIds`, method/type indexes, import aliases, and inheritance facts from the resolution catalog.
- [x] Emit nodes and outgoing edges only for changed files.
- [x] Continue deleting graph data only for changed/deleted files.
- [x] Permit edges from changed sources to unchanged targets only when the canonical target ID is proven.
- [x] Mark `usedFullResolutionCatalog=true` only when the full-catalog loading path was used.

### 3.2 Bound incremental cost

- [ ] Measure project-load time and memory for each TypeScript root.
- [x] Reuse project-root TypeScript discovery and exclude generated/build artifact paths.
- [x] Avoid parsing unrelated repository roots.
- [ ] Add a bounded fallback when tsconfig loading fails; persist the partial-catalog reason explicitly.
- [x] Do not claim a full catalog merely because the changed batch completed successfully.

### 3.3 Preserve scope-specific health

- [ ] Keep one stable index-run identity per TypeScript resolution scope and mode.
- [ ] Ensure an incremental run supersedes the prior incremental record for the same scope.
- [ ] Retain the latest full baseline for comparison.
- [ ] Verify a full run replaces partial warnings after normal non-destructive refresh.
- [ ] Verify incremental health does not aggregate stale scopes that no longer exist in project discovery.

## Phase 4: Improve Health Evidence And Trust Semantics

### 4.1 Make reason distribution actionable

- [x] Return the top bounded reason counts per current scope from freshness/drift output.
- [x] Show counts and percentages for calls and references separately.
- [ ] Distinguish production and test-source samples.
- [ ] Prefer deterministic diverse samples across reason, receiver shape, and file role instead of the first three occurrences only.
- [x] Keep structured output bounded and continue excluding source bodies/properties.

### 4.2 Separate catalog confidence from resolution quality

Represent two independent facts:

- catalog completeness: full, partial, unknown
- resolution quality: resolved/external/unresolved/indeterminate rates

Tasks:

- [x] Preserve the existing overall confidence for compatibility.
- [ ] Add typed subfields only if clients need to distinguish the two dimensions.
- [x] Explain whether `Low` comes from a partial catalog, known local failures, a broad indeterminate rate, or multiple causes.
- [x] Do not allow a healthy full scope to hide a broken partial scope.

### 4.3 Calibrate High/Medium/Low after resolver improvements

The current unresolved-local low-confidence threshold is `1`.

- [x] Keep the threshold unchanged during Phases 1–3 so progress is visible.
- [ ] Measure precision of remaining unresolved-local samples.
- [ ] Decide whether confidence should use absolute counts, rates, severity-weighted reasons, or a combination.
- [ ] Keep High reserved for full catalogs with no known actionable local failures.
- [ ] Consider Medium for a small bounded unresolved rate only if samples prove the remaining cases are unavoidable ambiguity rather than missing useful edges.
- [ ] Add explicit boundary tests for every adopted threshold.
- [ ] Document threshold rationale and prevent silent configuration drift.

Raising the threshold without evidence is explicitly rejected.

## Phase 5: Focused Test Plan

### Existing shields

Graph evidence is incomplete because relationship completeness is Low, so these shields are partly heuristic and must be verified directly:

- `tests/CodeMeridian.Indexer.Tests/Roslyn/CSharpAstWalkerTests.cs`
- `tests/CodeMeridian.RoslynIndexer.Tests/Pipeline/CSharpCallEdgeResolverTests.cs`
- `tests/CodeMeridian.RoslynIndexer.Tests/Pipeline/CSharpReferenceEdgeResolverTests.cs`
- `tests/CodeMeridian.RoslynIndexer.Tests/Pipeline/CSharpIncrementalIndexerTests.cs`
- `tools/TsIndexer/tests/walker.graph.test.ts`
- `tools/TsIndexer/tests/type-script-indexer-application.test.ts`
- `tests/CodeMeridian.Indexer.Tests/Cli/TypeScriptIndexerCommandBuilderTests.cs`
- `tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceRelationshipTrustTests.cs`
- `tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceFindGraphDriftTests.cs`

### Smallest useful test sequence

1. Receiver-evidence unit tests for every supported and rejected syntax shape.
2. C# resolver unit tests for declaring type, arity, extension, inheritance, and ambiguity.
3. TypeScript walker tests using changed source plus unchanged resolution source.
4. Indexer orchestration tests proving the full catalog is supplied without re-emitting unchanged files.
5. Application trust tests for catalog/quality combinations and reason rendering.
6. One end-to-end fixture index comparing full and incremental graph edges and health metadata.

### Required regression cases

- [x] Every raw candidate still receives exactly one primary disposition.
- [x] Duplicate resolution remains resolved-local with duplicate collapse reported separately.
- [x] Synthetic edges remain outside attempted-candidate accounting.
- [x] Unknown receivers never use unsafe global name-only fallback.
- [x] External receiver collisions remain external/unindexed.
- [x] `params`, optional, extension, and generic arity cases select only compatible targets.
- [ ] Full and incremental TypeScript runs resolve the same changed-file-to-unchanged-target edge.
- [x] Incremental runs do not re-ingest unchanged nodes.
- [ ] Deleted files remove their nodes/edges without corrupting the resolution catalog.
- [x] Failure samples stay deterministic, bounded, and source-body-free.
- [x] Structured freshness schema remains valid and compatible.

### Targeted commands

```powershell
dotnet test tests/CodeMeridian.Indexer.Tests/CodeMeridian.Indexer.Tests.csproj --no-restore --filter "FullyQualifiedName~CSharpAstWalker|FullyQualifiedName~TypeScriptIndexer"
dotnet test tests/CodeMeridian.RoslynIndexer.Tests/CodeMeridian.RoslynIndexer.Tests.csproj --no-restore --filter "FullyQualifiedName~CSharpCallEdgeResolver|FullyQualifiedName~CSharpReferenceEdgeResolver|FullyQualifiedName~CSharpIncrementalIndexer"
dotnet test tests/CodeMeridian.Application.Tests/CodeMeridian.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~RelationshipTrust|FullyQualifiedName~FindGraphDrift|FullyQualifiedName~CheckGraphFreshness"
npm test --prefix tools/TsIndexer -- --run tests/walker.graph.test.ts tests/type-script-indexer-application.test.ts
```

### Broader commands

```powershell
dotnet test CodeMeridian.sln --no-restore
npm test --prefix tools/TsIndexer
npm test --prefix tools/HtmlCssIndexer
```

## Phase 6: Benchmark And Precision Gates

### 6.1 Edge precision review

- [ ] Compare all newly recovered fixture edges to expected canonical targets.
- [ ] Sample newly recovered live edges by reason family and file role.
- [ ] Require no false local edges in the curated corpus.
- [ ] Reject any rule whose precision depends only on method-name uniqueness.
- [ ] Store aggregate evidence, not source bodies.

### 6.2 Performance budgets

- [ ] Record full and incremental C# indexing duration and peak memory before/after.
- [ ] Record each TypeScript root's project-load, walk, and ingest duration.
- [ ] Set an incremental latency budget only after baseline measurement.
- [ ] Ensure semantic fallback failures are bounded and observable.
- [ ] Avoid `Thread.Sleep`-based performance tests; use measured integration runs outside deterministic unit assertions.

### 6.3 Graph usefulness checks

After each slice, verify representative tools:

- [ ] `find_impact` gains expected callers without unrelated callers.
- [ ] `find_test_shield` gains direct/primary evidence where fixtures prove it.
- [ ] `build_minimal_context` returns useful bounded relationships.
- [ ] empty results still carry honest relationship-completeness warnings.
- [ ] `check_graph_freshness` and `find_graph_drift` agree on totals and reasons.

## Phase 7: Rollout And Live Acceptance

### 7.1 Safe rollout order

1. Ship reason-reporting and fixture coverage.
2. Ship syntax-only C# receiver improvements.
3. Ship resolver ranking/arity/inheritance improvements.
4. Ship full-catalog TypeScript incremental resolution.
5. Evaluate semantic C# enrichment separately.
6. Calibrate trust thresholds last.

Each slice must be independently revertible.

### 7.2 Indexing procedure

- [ ] Deploy the updated indexer/server components required by the slice.
- [ ] Run `codemeridian index . --project CodeMeridian --force-full` without `--clear`.
- [ ] Capture full per-scope outcome evidence.
- [ ] Run the same force-full command again and compare deterministic totals.
- [ ] Make a controlled small change and run normal incremental indexing.
- [ ] Verify the incremental run uses a full resolution catalog for each supported scope.
- [ ] Re-run freshness, drift, impact, test-shield, and minimal-context acceptance.

### 7.3 Live acceptance gates

- [ ] No accounting invariant failures.
- [ ] No increase in false local edges in reviewed samples.
- [ ] No partial-catalog warning for normal TypeScript incremental runs.
- [ ] C# reason targets meet the agreed slice threshold.
- [ ] Freshness/drift totals equal persisted current-run totals.
- [ ] Full/incremental runs remain deterministic.
- [ ] Existing 62-tool MCP inventory and eight structured schemas remain valid.
- [ ] Full .NET and TypeScript regressions pass.

## Rollback

- [ ] Keep existing schema-v2 property names readable for at least one compatibility window.
- [ ] Gate semantic enrichment independently from syntax-only indexing if it is adopted.
- [ ] Revert one resolver rule at a time; do not revert exact accounting or bounded evidence.
- [ ] Use a non-destructive force-full index after rollback.
- [ ] Reserve `--clear` for canonical-ID/schema changes or proven stale-node cleanup.
- [ ] Restore the prior trust policy if a threshold calibration creates unsafe confidence.

## Definition Of Done

- [ ] Root-cause fixtures reproduce the dominant live reason families.
- [x] C# receiver evidence and candidate selection improve without unsafe fallback.
- [x] TypeScript incremental resolution uses a full catalog and emits only changed-file data.
- [ ] Remaining unresolved/indeterminate cases have accurate, bounded, actionable reasons.
- [ ] Confidence policy reflects measured catalog completeness and resolution quality.
- [ ] Targeted, full, and live acceptance suites pass.
- [ ] Two full and two identical incremental runs are deterministic.
- [x] Documentation states supported resolution guarantees and known limitations honestly.
- [ ] The live baseline, after metrics, and any accepted residual risks are recorded in this plan.

## Recommended First Implementation Slice

Start with the smallest high-confidence work:

1. Add receiver fixture coverage for predefined types, lambdas, locals, conditional/chained access, and negative controls.
2. Extract receiver evidence from `CSharpAstWalker` into a focused collaborator.
3. Add safe syntax support for predefined receivers and explicit variable scopes.
4. Prefer exact same-declaring-type candidates for unqualified calls.
5. Add `params` and extension-method arity metadata/tests.
6. Run the full C# fixture and compare reason histograms before considering semantic enrichment.

This slice attacks the largest measured reason families without changing trust thresholds or requiring a new compilation-loading architecture.
