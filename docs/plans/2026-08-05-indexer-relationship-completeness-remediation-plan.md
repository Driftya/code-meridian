# Indexer Relationship Completeness Remediation Plan

- Date: 2026-08-05
- Status: implementation complete for the safe syntax/catalog/reporting scope; locally verified and live-indexed, with C# semantic enrichment and remote server deployment recorded as follow-up work
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
- [ ] Reduce C# `unknown_member_receiver` materially on the CodeMeridian fixture without unsafe name-only fallback. The syntax-only slice did not meet this outcome; see Final Live Evidence.
- [x] Resolve common same-type, lambda/local-variable, chained-receiver, `params`, extension, and inherited-member cases when evidence is sufficient.
- [x] Make TypeScript incremental runs use a complete resolution catalog while emitting and deleting only changed-file graph data.
- [x] Keep external/framework calls from becoming noisy local graph edges.
- [x] Make full and incremental relationship results comparable by scope and reason.
- [x] Calibrate High/Medium/Low only after precision gates pass. The threshold remains `1`; High still requires zero known local failures.
- [x] Keep all failure samples bounded and free of source bodies, argument values, credentials, and arbitrary graph properties.

### Initial quantitative targets

These are investigation gates, not promises to manufacture edges:

- [ ] Reduce C# indeterminate rate from 40.6% of C# candidates to below 20% in the first safe slice.
- [x] Reduce C# unresolved-local rate from 5.2% to below 3% without reducing fixture precision. Final live rate: 2.3%.
- [x] Eliminate partial-catalog warnings for normal TypeScript incremental indexing.
- [x] Eliminate `local_declaration_not_indexed` caused solely by unchanged files being absent from the incremental catalog.
- [x] Achieve 100% precision on the curated positive/negative receiver-resolution fixture.
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

- [x] `dotnet test CodeMeridian.sln --no-restore`: 1,014 passed, 6 skipped live-acceptance tests, 0 failed.
- [x] `npm test` in `tools/TsIndexer`: 71 passed, 0 failed.
- [x] `npm test --prefix tools/HtmlCssIndexer`: 7 passed, 0 failed.
- [x] `npm run build` in `tools/TsIndexer`: passed and refreshed tracked `dist` assets.
- [x] `git diff --check`: no whitespace errors.

Final closure of the safe implementation scope:

- [x] Ran the updated indexer from source with non-destructive force-full C# and TypeScript passes and captured after metrics.
- [x] Added deterministic repeated full/incremental fixture replays. Two identical live replays remain a rollout check after server deployment.
- [x] Measured the C# targets: unresolved-local passed at 2.3%; indeterminate failed at 44.0%.
- [x] Captured TypeScript catalog latency and heap evidence per active root and on a one-file incremental run.
- [x] Closed the semantic-enrichment gate for this slice: syntax-only remains shipped; the live residual proves semantic or cross-file type enrichment needs a separate bounded design/benchmark before adoption.
- [x] Retained the unresolved-local threshold at `1` after live sample review. The remaining failures are actionable, so raising it would create unsafe confidence.

Rollout follow-up:

- [ ] Deploy/restart the updated MCP server so active TypeScript scope filtering is used by live freshness/drift consumers. The configured remote server still runs the pre-change reader and therefore reports the removed Evolution scope.
- [ ] After deployment, run two identical live full passes and two identical live incremental replays. Local repeated-replay tests are deterministic, but this operational gate was not claimed from dissimilar live batches.

## Non-Goals

- [x] Do not create graph nodes for every framework or NuGet/npm member.
- [x] Do not resolve unknown receivers by globally unique method name alone.
- [x] Do not treat external/unindexed outcomes as defects without local evidence.
- [x] Do not run `--clear` for ordinary relationship refreshes.
- [x] Do not require a successful build for basic syntax indexing; semantic enrichment must degrade safely.
- [x] Do not load or persist source bodies as diagnostic evidence.
- [x] Do not combine C#, TypeScript, HTML/CSS, and external-call modeling into one universal resolver.

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

- [x] Add or reuse a bounded CLI/GraphQL report that prints one row per language/scope/mode.
- [x] Include attempted, resolved-local, external/unindexed, unresolved-local, indeterminate, duplicate, synthetic, scanned-file, and catalog-completeness fields.
- [x] Include reason histograms separately for calls and type references.
- [x] Include percentages as well as counts so repository growth does not look like a regression by itself.
- [x] Include production/test file-role breakdowns for unresolved and indeterminate outcomes.
- [x] Never print API keys, source bodies, argument text, or arbitrary property dictionaries.

### 0.2 Create a deterministic relationship fixture corpus

Add small checked-in fixtures or inline source cases covering:

- [x] predefined static receivers: `string`, `int`, `DateTime`, and aliases
- [x] typed parameters, fields, properties, locals, `var`, object creation, casts, and parenthesized receivers
- [x] conditional access and null-forgiving receivers
- [x] lambda parameters, local functions, `foreach`, `catch`, `using`, pattern variables, and deconstruction
- [x] fluent/chained calls and calls on method-return values
- [x] same-type unqualified calls and overloaded helpers
- [x] optional parameters, `params`, generic methods, and extension methods
- [x] local inheritance/interface members and external-base members
- [x] ambiguous same-name candidates across namespaces/files
- [x] negative controls where no safe local edge exists

For every case, assert both the selected edge and the disposition/reason when no edge is selected.

### 0.3 Capture before metrics

- [x] Run a non-destructive force-full index.
- [x] Capture outcome totals and reason histograms.
- [ ] Run the same force-full index again and require identical relationship totals.
- [x] Replay fixed full and incremental fixture inputs twice and require identical totals.
- [x] Store the command, indexer version, commit, timestamp, and normalized results in implementation evidence.

## Phase 1: Improve C# Receiver Evidence

### 1.1 Extract receiver analysis from the large walker

`CSharpAstWalker.cs` is already well above the preferred file size. Keep the change reviewable:

- [x] Extract invocation/receiver evidence into one focused internal collaborator.
- [x] Keep syntax walking and node ownership in `CSharpAstWalker`.
- [x] Return a small typed evidence record: call name, supplied argument count, receiver kind, receiver type hint, declaring-type hint, generic arity, and confidence/source.
- [x] Keep one public type per file and avoid a universal relationship framework.
- [x] Preserve current edge-property names for compatibility; add fields only when required.

### 1.2 Cover missing syntax-only receiver shapes

- [x] Recognize predefined static receiver syntax such as `string.IsNullOrWhiteSpace`.
- [x] Unwrap parentheses, null-forgiving expressions, casts, `await`, conditional access, and object creation.
- [x] Track explicitly typed local variables and improve safe `var` inference from object creation and casts. Known-local-call return inference remains part of the semantic-enrichment gate.
- [x] Track lambda, anonymous-method, `foreach`, `catch`, `using`, pattern, and deconstruction variables when their types are explicit or safely inferable.
- [x] Preserve receiver evidence through member-access chains when an intermediate current-type member type is known.
- [x] Do not infer a local type from capitalization alone when aliases or namespaces make the result ambiguous.

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
- [x] Distinguish true incompatible arity from insufficient extraction metadata.

### 2.3 Handle inherited and external-base members

- [x] Build a bounded local type hierarchy catalog from exact `Inherits`/`Implements` evidence.
- [x] Resolve members declared on indexed local bases/interfaces.
- [x] When a local receiver inherits an external base and no local member exists, classify conservatively as external/unindexed or indeterminate rather than automatically unresolved-local.
- [x] Keep a local failure when exact local interface/base evidence promises the missing member.
- [x] Add `CreateClient()`-style framework inheritance fixtures and local-inheritance negative controls.

### 2.4 Normalize type identity

- [x] Compare canonical namespace-qualified type identity when available.
- [x] Normalize generic type display, nullable suffixes, aliases, arrays, and nested types.
- [x] Use imports/usings and namespace evidence where available.
- [x] Avoid merging distinct same-short-name types.

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

- [x] Measure project-load time and memory for each TypeScript root.
- [x] Reuse project-root TypeScript discovery and exclude generated/build artifact paths.
- [x] Avoid parsing unrelated repository roots.
- [x] Add a bounded fallback when tsconfig loading fails; persist the partial-catalog reason explicitly.
- [x] Do not claim a full catalog merely because the changed batch completed successfully.

### 3.3 Preserve scope-specific health

- [x] Keep one stable index-run identity per TypeScript resolution scope and mode.
- [x] Ensure an incremental run supersedes the prior incremental record for the same scope.
- [x] Retain the latest full baseline for comparison.
- [x] Verify a full run replaces partial warnings after normal non-destructive refresh.
- [x] Verify incremental health does not aggregate stale scopes that no longer exist in project discovery. The reader behavior is locally verified; remote consumers require deployment.

## Phase 4: Improve Health Evidence And Trust Semantics

### 4.1 Make reason distribution actionable

- [x] Return the top bounded reason counts per current scope from freshness/drift output.
- [x] Show counts and percentages for calls and references separately.
- [x] Distinguish production and test-source samples.
- [x] Prefer deterministic diverse samples across reason, receiver shape, and file role instead of the first three occurrences only.
- [x] Keep structured output bounded and continue excluding source bodies/properties.

### 4.2 Separate catalog confidence from resolution quality

Represent two independent facts:

- catalog completeness: full, partial, unknown
- resolution quality: resolved/external/unresolved/indeterminate rates

Tasks:

- [x] Preserve the existing overall confidence for compatibility.
- [x] Add typed subfields only if clients need to distinguish the two dimensions. No new public trust DTO fields were needed; persisted catalog evidence and explanations keep the dimensions visible.
- [x] Explain whether `Low` comes from a partial catalog, known local failures, a broad indeterminate rate, or multiple causes.
- [x] Do not allow a healthy full scope to hide a broken partial scope.

### 4.3 Calibrate High/Medium/Low after resolver improvements

The current unresolved-local low-confidence threshold is `1`.

- [x] Keep the threshold unchanged during Phases 1–3 so progress is visible.
- [x] Measure precision of remaining unresolved-local samples.
- [x] Decide whether confidence should use absolute counts, rates, severity-weighted reasons, or a combination. Retain the absolute known-local-failure gate for this release.
- [x] Keep High reserved for full catalogs with no known actionable local failures.
- [x] Consider Medium for a small bounded unresolved rate only if samples prove the remaining cases are unavoidable ambiguity rather than missing useful edges. Rejected for the current evidence.
- [x] Add explicit boundary tests for every adopted threshold.
- [x] Document threshold rationale and prevent silent configuration drift.

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
- [x] Full and incremental TypeScript runs resolve the same changed-file-to-unchanged-target edge.
- [x] Incremental runs do not re-ingest unchanged nodes.
- [x] Deleted files remove their nodes/edges without corrupting the resolution catalog.
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

- [x] Compare all newly recovered fixture edges to expected canonical targets.
- [x] Sample newly recovered live edges by reason family and file role.
- [x] Require no false local edges in the curated corpus.
- [x] Reject any rule whose precision depends only on method-name uniqueness.
- [x] Store aggregate evidence, not source bodies.

### 6.2 Performance budgets

- [ ] Record full and incremental C# indexing duration and peak memory before/after.
- [x] Record each TypeScript root's catalog-load duration and heap use plus end-to-end live duration.
- [x] Set an incremental latency budget only after baseline measurement. No hard budget was adopted from one remote run; the measured baseline is retained below.
- [x] Ensure catalog fallback failures are bounded and observable; semantic enrichment was not adopted.
- [x] Avoid `Thread.Sleep`-based performance tests; use measured integration runs outside deterministic unit assertions.

### 6.3 Graph usefulness checks

After each slice, verify representative tools:

- [x] `find_impact` returns bounded confidence-aware evidence and retains the Low-completeness warning.
- [x] `find_test_shield` finds the new direct resolver regression tests.
- [x] `build_minimal_context` returns the resolver, health collector, model, and direct tests within budget.
- [x] empty results still carry honest relationship-completeness warnings.
- [x] `check_graph_freshness` and `find_graph_drift` agree on totals and reasons on the deployed reader version.

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

- [ ] Deploy the updated indexer/server components required by the slice. The updated indexer ran from source; the remote MCP server reader still requires deployment.
- [x] Run `codemeridian index . --project CodeMeridian --force-full` without `--clear`.
- [x] Capture full per-scope outcome evidence.
- [ ] Run the same force-full command again and compare deterministic totals.
- [x] Make a controlled small change and run normal incremental indexing.
- [x] Verify the incremental run uses a full resolution catalog for the changed TypeScript scope.
- [x] Re-run freshness, drift, impact, test-shield, and minimal-context acceptance.

### 7.3 Live acceptance gates

- [x] No accounting invariant failures.
- [x] No increase in false local edges in reviewed fixtures and bounded samples.
- [x] No partial-catalog warning for normal TypeScript incremental runs.
- [ ] C# reason targets meet the agreed slice threshold.
- [x] Freshness/drift totals equal persisted current-run totals; the old deployed reader also includes one stale scope until redeployed.
- [x] Full/incremental fixture replays remain deterministic. Identical live replay verification remains a rollout item.
- [x] Existing 62-tool MCP inventory and reviewed structured-schema baseline remain valid in the full local suite.
- [x] Full .NET and TypeScript regressions pass.

## Final Live Evidence: 2026-08-05

### C# after metrics

The corrected non-destructive C# force-full pass completed in 171.1 seconds across 471 files.

| Metric | Plan baseline | Final live | Result |
|---|---:|---:|---|
| Attempted relationships | 16,665 | 17,701 | repository grew during implementation |
| Resolved local | 4,486 | 4,894 | +408 useful local resolutions |
| External/unindexed | 4,546 | 4,618 | expected classification |
| Unresolved local | 870 (5.2%) | 407 (2.3%) | target below 3% passed |
| Indeterminate | 6,763 (40.6%) | 7,782 (44.0%) | target below 20% failed |
| Duplicate candidates | not a failure | 178 | reported separately |
| Synthetic edges | not a failure | 193 | outside raw attempt accounting |

The short-name/canonical-name regression discovered during live acceptance was fixed before the final pass: `ambiguous_local_target` fell from the regressed 703 to 53. The remaining 7,763 `unknown_member_receiver` calls are concentrated in syntax-only cases such as `var` initialized from method returns, fluent/assertion chains, and members declared in another partial-class file. Production/test file-role evidence is persisted separately: 2,403 production and 5,362 test indeterminate outcomes; 146 production and 261 test unresolved-local outcomes.

This evidence rejects threshold relaxation and closes the semantic decision gate for this slice: do not add an unbenchmarked `MSBuildWorkspace` dependency here. A follow-up semantic/cross-file enrichment design must prove bounded project loading, syntax fallback, and recovered-edge precision before adoption.

### TypeScript after metrics

The full TypeScript-only pass completed in 293.8 seconds end to end against the remote server. Per-root catalog evidence was:

| Active root | Files | Catalog load | Heap used |
|---|---:|---:|---:|
| HtmlCssIndexer | 13 | 2,772 ms | 217,036,280 bytes |
| IndexerShared | 9 | 2,290 ms | 190,842,400 bytes |
| TsIndexer | 35 | 4,362 ms | 244,138,888 bytes |

A controlled one-file TsIndexer incremental pass completed in 9.1 seconds, emitted 7 nodes and 17 edges, loaded all 35 source files into the resolution catalog in 2,501 ms using 217,217,272 heap bytes, and reported 66 attempted relationships: 8 resolved local, 58 external/unindexed, 0 unresolved local, and 0 indeterminate. `usedFullResolutionCatalog=true` was persisted.

The active-scope catalog lists HtmlCssIndexer, IndexerShared, and TsIndexer. Local reader tests prove the removed Evolution root is ignored, including an empty-catalog case. The configured remote MCP server still runs the old reader, so its live freshness/drift output continues to aggregate the historical Evolution run until that server is rebuilt and restarted.

### Verification evidence

- Acceptance source base: `b70efa08` plus the working-tree implementation described by this plan; TypeScript indexer package version `1.0.12`.
- C# live command: `dotnet run --project tools/Indexer/CodeMeridian.Indexer.csproj -- index . --project CodeMeridian --force-full --skip-typescript --no-docs --skip-config --skip-diagnostics --skip-keywords`; persisted completion `2026-08-05T15:35:23Z`.
- TypeScript incremental command: `dotnet run --project tools/Indexer/CodeMeridian.Indexer.csproj -- index . --project CodeMeridian --skip-csharp --no-docs --skip-config --skip-diagnostics --skip-keywords`; persisted completion `2026-08-05T15:29:13Z`.
- Normalized after evidence was captured with `codemeridian report relationship-health --project CodeMeridian --format json`.
- `dotnet test CodeMeridian.sln --no-restore`: 1,014 passed, 6 expected live MCP tests skipped, 0 failed.
- `npm test --prefix tools/TsIndexer`: 71 passed, 0 failed.
- `npm test --prefix tools/HtmlCssIndexer`: 7 passed, 0 failed.
- `npm run build --prefix tools/TsIndexer`: passed and refreshed tracked `dist` assets.
- `git diff --check`: passed.
- Post-index CodeMeridian checks found High node freshness for the resolver, direct resolver regression shields, and useful bounded impact/minimal-context evidence. Relationship completeness correctly remained Low.

## Rollback

- [x] Keep existing schema-v2 property names readable for at least one compatibility window.
- [x] Gate semantic enrichment independently from syntax-only indexing if it is adopted.
- [x] Keep resolver rules independently revertible; do not revert exact accounting or bounded evidence.
- [x] Use a non-destructive force-full index after rollback.
- [x] Reserve `--clear` for canonical-ID/schema changes or proven stale-node cleanup.
- [x] No trust-policy rollback is needed because the threshold was not changed.

## Definition Of Done

- [x] Root-cause fixtures reproduce the dominant safely addressable live reason families; semantic-only receiver provenance remains a documented follow-up.
- [x] C# receiver evidence and candidate selection improve without unsafe fallback.
- [x] TypeScript incremental resolution uses a full catalog and emits only changed-file data.
- [x] Remaining unresolved/indeterminate cases have accurate, bounded, actionable reasons.
- [x] Confidence policy reflects measured catalog completeness and resolution quality.
- [x] Targeted and full local suites pass; live C# full, TypeScript full, and TypeScript incremental passes completed.
- [x] Two full and two incremental fixture replays are deterministic; identical live replay remains rollout verification after deployment.
- [x] Documentation states supported resolution guarantees and known limitations honestly.
- [x] The live baseline, after metrics, and accepted residual risks are recorded in this plan.

## Recommended First Implementation Slice

Start with the smallest high-confidence work:

1. Add receiver fixture coverage for predefined types, lambdas, locals, conditional/chained access, and negative controls.
2. Extract receiver evidence from `CSharpAstWalker` into a focused collaborator.
3. Add safe syntax support for predefined receivers and explicit variable scopes.
4. Prefer exact same-declaring-type candidates for unqualified calls.
5. Add `params` and extension-method arity metadata/tests.
6. Run the full C# fixture and compare reason histograms before considering semantic enrichment.

This slice attacks the largest measured reason families without changing trust thresholds or requiring a new compilation-loading architecture.
