# Project Capabilities: Remaining Delivery Checklist

Date: 2026-10-19

Status: active. **18 completed items (12 foundation, 6 delivery); 34 open delivery items.**

This is the current progress tracker for the five October 8 plans. Their detailed designs are retained under [references/project-capabilities/](references/project-capabilities/). This checklist consolidates overlapping work; it does not replace their semantics or expand their MVPs.

The baseline includes committed and uncommitted implementation in this working tree. Checked items mean implemented with identified verification evidence, not deployed. Unchecked items can have partial groundwork but have not met their completion criteria. Counts describe checklist items, not equal-sized work or an effort percentage. Keep IDs stable, update the counts when a box changes, and record the verifying test or acceptance result before checking an item.

## Progress

| Workstream | Completed | Open | Current state |
|---|---:|---:|---|
| Discovery and producer evidence | 10 | 10 | Worker identities, source binding, and C# execution reports implemented; semantic worker coverage remains unaudited |
| Capability-aware routing | 6 | 3 | Shared guards, enabled-section reports, and scoped document search implemented; scoped graph queries and conformance remain |
| Repository constraints and Markdown | 2 | 7 | Validated contracts and inventory-only placement implemented; public query and Markdown structural evidence remain open |
| Offline PowerShell | 0 | 10 | Detected but unsupported; parser worker not implemented |
| Shared release acceptance | 0 | 4 | Existing tests cover delivered slices; full roadmap acceptance remains |
| **Total** | **18** | **34** | **52 tracked items** |

## Completed Baseline

- [x] I01 — Local `profile` discovery classifies accepted files and observed roots, respects exclusions/reparse boundaries, and bounds examples and warnings. Evidence: `RepositoryInventoryDiscovery`, `LocalProjectProfileBuilder`, and their discovery/profile fixtures.
- [x] I02 — Observed content, analyzer support/configuration, and indexing readiness are distinct; disabled/skipped analyzers do not hide files, and PowerShell detection does not claim support. Evidence: `ProjectProfile`, `LocalProjectProfileBuilderTests`.
- [x] I03 — Server-issued generations support atomic complete-inventory publication, idempotent retries, stale/conflict rejection, and retention on incomplete discovery. Evidence: `ProjectProfileSnapshot`, `Neo4jProjectProfileRepository`, profile publication/persistence tests.
- [x] I04 — Authenticated API/SDK and structured MCP profile reads expose bounded summaries and target lookup; missing/stale profiles remain explicit. Evidence: `ProjectProfileService`, `ProjectProfileClientTests`, `ProjectProfileEndpointTests`.
- [x] I05 — Analyzer running/terminal outcomes are generation-scoped; failed, partial, delta, and unaudited completion do not establish complete coverage. Evidence: `IndexProfileRun`, outcome contracts/storage, `IndexProfileRunReporterTests`.
- [x] I06 — Incremental/no-change/watch paths reconcile server identity, generation, observation, and known fingerprints; failures do not advance the cache. Evidence: `IndexProfileReconciliationTests`, `IncrementalIndexCacheTests`.
- [x] I07 — Optional Git observations, resolved configuration hashes, and loaded managed implementation/parser fingerprints flow through profiles and outcomes. Evidence: `ProjectRevisionReaderTests`, `ProjectAnalysisFingerprintTests`. Node worker identities are recorded in A01; content binding is recorded in A02.
- [x] I08 — Document read/upload failures affect outcomes; replacement removes stale chunks; search excludes removed inventory sources and rechecks generation. Evidence: `DocumentOutcomeTests`, `DocumentIndexRunCoordinatorTests`, document routing tests.
- [x] I09 — Catalog requirements and deterministic per-step planning use project/target evidence; document edits avoid callable resolution, unavailable optional steps are visible, and required steps cannot be truncated into success. Evidence: `ContextWorkflowToolRequirements`, `ContextWorkflowToolPolicy`, `ToolCapabilityPolicyTests`, `ProjectCapabilityRoutingTests`.
- [x] I10 — Shared Application guards protect direct query families; results distinguish unavailable analysis from executed empty findings, and context packs omit unaudited test sections. Evidence: `DirectCapabilityGuardTests`, `KeywordCapabilityGuardTests`, structured MCP schema tests.
- [x] I11 — Execution revalidates readiness/generation, resolves exact targets, preserves required ordering, and discards stale or unverified cross-project results. Evidence: `DirectCapabilityGuardTests`, `CodebaseQueryServiceContextWorkflowTests`.
- [x] I12 — Applicability contracts, tool descriptions, dependency-impact metadata, feature docs, and bundled agent guidance describe the delivered routing behavior. Evidence: `docs/context-workflows.md`, `ToolDependencyCatalog`, MCP schema snapshots, bundled capability packs. New feature guidance remains E04.

## A — Finish Discovery And Producer Evidence

Reference: [discovery and capability profiles](references/project-capabilities/2026-10-08-project-discovery-capability-profile-plan.md).

- [x] A01 — Report actual TypeScript/JavaScript, frontend, and SQL worker/parser/runtime identities from the executing workers; publish and compare those fingerprints. Evidence: `worker-identity.test.ts`, `worker-identity-process.test.ts`, `NodeWorkerIdentityTests`, and SQL cache fixtures verify actual installed versions/runtime bytes, source and compiled entrypoints, profile/outcome publication, missing-identity rejection, identity-change rejection before indexing, and cache invalidation. See the October 20 delivery evidence below.
- [x] A02 — Bind outcomes to analyzed file content/revisions, not only observational Git state. Evidence: `AnalyzedSourceSnapshotTests`, `IndexProfileRunReporterTests`, `IndexProfileReconciliationTests`, Core validation/round-trip tests, capability routing tests, publication fingerprint tests, and MCP/API/SDK/schema fixtures cover unchanged/changed/deleted/added inputs, transient different-byte reads, delayed publication, upload-time invalidation, cache retention, and legacy evidence rejection. See the October 20 source-binding evidence below.
- [ ] A03 — Audit C# definitions/dependencies and publish trustworthy per-scope coverage, including project loading, skipped/failed files, diagnostics, and legitimate zero-edge results. Done when worker reports drive readiness without inferring it from process exit. In progress: snapshot-verified single reads, execution reports, bounded parse/semantic diagnostics, skipped/failed input accounting, relationship summaries, callable syntax/identity accounting, and partial-outcome publication are implemented. Dynamic receiver calls are explicitly indeterminate. Known unrepresented callable forms and conflicting identities produce partial evidence. Evaluated project membership/loading, callable representation/identity gaps, and complete dependency extraction remain open; clean accounting still publishes unknown coverage. See the October 21 and October 22 evidence below.
- [ ] A04 — Audit TypeScript/JavaScript definitions/dependencies and root membership, including imports and unresolved constructs. Done when partial roots and skipped/failed files cannot authorize full project queries.
- [ ] A05 — Audit frontend relationship coverage and separately define/publish cascade evidence. Done when generic frontend relationships cannot authorize unsupported specificity/source-order analysis.
- [ ] A06 — Audit configuration definitions, code/config usage, and configured architecture-rule evidence as separate capabilities. Done when definition-only or invalid-policy inputs cannot claim usage/rule readiness.
- [ ] A07 — Audit SQL outcomes per supported dialect/scope, including parse/publication failures and owned dependency facts. Done when SQL object evidence is explicit and does not grant generic callable compatibility.
- [ ] A08 — Publish audited test-to-code and namespace-classification capabilities for each supported language. Done when fixture-backed producer reports, rather than generic dependency edges or test file roles, establish those capabilities.
- [ ] A09 — Publish audited graph-projection and embedding evidence, including projection node/edge filters and embedding completeness. Done when mixed SQL/frontend content and provider failure cannot silently inherit callable/semantic coverage.
- [ ] A10 — Add scoped producer evidence for change/architecture history and keyword relationships. Done when those queries distinguish absent/unavailable evidence from executed empty results and reject stale producer identities.
- [ ] A11 — Define safe last-good evidence reuse across delta generations. Done when unchanged verified facts remain usable only with content/config/parser/inventory binding, while failed/deleted/rebound scopes invalidate the affected evidence.
- [ ] A12 — Complete per-worker/root membership and project aggregation rules. Done when target/root/project coverage is explicit, overlapping or incomplete roots cannot imply full coverage, and configured architecture applicability is discoverable without assuming default rules ran.

## B — Finish Routing And Scope Refinements

Reference: [capability-aware tool routing](references/project-capabilities/2026-10-08-capability-aware-tool-routing-plan.md).

- [x] B01 — Evaluate composite report requirements from enabled sections/flags. Evidence: `CompositeCapabilityGuardTests`, `PrContextReportCapabilityTests`, `CompositeReportEndpointTests`, `StatusApiPrContextReportTests`, weather-report fixtures, and PR CLI fixtures verify disabled reads, requested-section blocking, independent advisory projection/document coverage, stale-result rejection, cancellation, API/SDK/MCP forwarding, and incomplete CLI exit/output. See the October 22 B01 evidence below.
- [x] B02 — Add root/target-filtered document query storage and transport support. Evidence: `DocumentQueryScopeTests`, `ScopedDocumentPolicyTests`, `ScopedDocumentSearchTests`, `ScopedDocumentStorageTests`, `ScopedDocumentSearchEndpointTests`, and `DocumentSearchClientTests` verify exact/descendant boundaries, case/path validation, matching complete root coverage, overlapping failures, stale-result rejection, bounded storage, workflow scope, authenticated API/SDK and MCP forwarding/schema. The Neo4j scoped-search integration fixture builds; live acceptance remains E02. See the October 22 B02 evidence below.
- [ ] B03 — Add root-filtered graph queries where tool semantics allow them, with scope-aware revalidation. Done when unrelated roots cannot contribute facts or readiness and cross-root traversals have a documented evidence boundary.
- [ ] B04 — Audit existing SQL/frontend query conformance per tool: identifiers, node kinds, edges, filters, snippets, projection behavior, and response types. Done when a compatibility matrix and focused tests enable only proven combinations; matching edge names alone are insufficient.
- [ ] B05 — Integrate placement and Markdown-reference capabilities into plans, direct guards, and reports after C is implemented. Done when document-only and mixed targets receive appropriate per-rule outcomes without namespace or callable prerequisites. PowerShell activation is tracked separately in D09.

## C — Build Repository Constraints And Markdown Validation

Reference: [repository constraints and Markdown](references/project-capabilities/2026-10-08-repository-constraints-markdown-plan.md).

- [x] C01 — Define versioned parser-independent group, placement, reference, exception, severity, and outcome contracts plus configuration validation. Evidence: `RepositoryConstraintPolicyTests`, `ArchitectureRuleSetConfigurationTests`, `ConfigurationArchitecturePolicyTests`, and `ArchitecturePolicyLoadingTests` verify versioned round trips, strict policy validation, legacy namespace behavior, pre-upload rejection, and absence-only default fallback. See the October 22 C01-C02 evidence below.
- [x] C02 — Implement inventory-only placement evaluation with overlapping groups, allowed/denied paths, explicit exceptions, and normalized case-aware paths. Evidence: `RepositoryPlacementEvaluatorTests` covers Markdown/SQL/PowerShell/assets without semantic indexers, overlapping groups, deny precedence, explicit exceptions, case/separators/spaces/dot segments/traversal, incomplete discovery, missing/excluded/unobserved targets, collision rejection, bounded findings, and cancellation. See the October 22 C01-C02 evidence below.
- [ ] C03 — Expose a bounded project/optional-target repository-constraint query through Application, API/SDK, and MCP. Done when rule IDs, reasons, evidence locations, applicability, and evaluation coverage round-trip with auth/cancellation/schema tests.
- [ ] C04 — Audit `DocumentReferenceExtractor` fidelity and select/pin a syntax-aware Markdown parser through a compatibility spike. Done when extraction fixtures establish the supported syntax and document search keeps its existing behavior; regex extraction is not treated as validation.
- [ ] C05 — Extract stable file-level reference facts with source spans, link/image kind, raw/normalized targets, fragments, parser fingerprint, and inventory generation. Done when inline/reference-style forms, code fences, escapes, HTML limits, URL decoding, and root-relative/anchor semantics have explicit fixtures.
- [ ] C06 — Resolve local references against inventory and target-group rules. Done when missing, forbidden, excluded/unobserved, external, fragment-only, unsupported, and out-of-repository cases are distinct; no remote fetch or outside-repository traversal occurs.
- [ ] C07 — Publish owned Markdown-reference evidence generation-safely and handle retries, changed links, renames/deletions, and parse failures. Done when failed/stale chunks cannot claim current structural coverage and searchable document chunks share stable file identity without becoming code symbols.
- [ ] C08 — Compose new per-rule results with existing architecture reports and capability profiles. Done when document-only fixtures evaluate placement/references while namespace rules are inapplicable, and incomplete inventory/parsing cannot produce clean validation.
- [ ] C09 — Complete constraint-specific verification and user guidance. Done when Core validation, parser/pipeline, Application, transport, and persistence fixtures cover path case/separators/spaces/traversal, policy errors, reference forms, failure/replay, and backward-compatible namespace behavior.

## D — Build Offline PowerShell Indexing

Reference: [PowerShell indexing](references/project-capabilities/2026-10-08-powershell-indexing-plan.md).

- [ ] D01 — Complete the .NET 10 native parser/package compatibility spike and pin the tested version. Done when clean Windows/Linux packaging and supported syntax/encoding work without depending on globally installed PowerShell; record the compatibility decision.
- [ ] D02 — Implement a dedicated parser worker for scripts/modules with functions, filters, lexical bodies, parameters, command occurrences, bounded extents/snippets, and exact parse diagnostics. Done when syntax/encoding/unsupported constructs have source-backed extraction fixtures.
- [ ] D03 — Define stable project/path/scope/kind/name identities and graph mapping. Done when duplicate declarations are deterministic, line moves do not become the sole identity, and reused node/edge kinds satisfy their existing query semantics.
- [ ] D04 — Implement conservative static binding for lexical definitions and supported script/dot-source paths. Done when command case, visibility, shadowing, redefinitions, working-directory semantics, narrowly supported `$PSScriptRoot`, and dynamic invocations produce resolved/unresolved/indeterminate/external outcomes honestly.
- [ ] D05 — Extract literal module imports/requires/using and `.psd1` exports/metadata without evaluation. Done when supported export visibility restricts binding and wildcard/computed exports or ambiguous modules retain explicit limits.
- [ ] D06 — Prove non-execution and bounded analysis behavior. Done when side-effect fixtures never run, profiles/modules are not loaded, no runspace/pipeline invocation occurs, and cancellation/path-boundary fixtures pass.
- [ ] D07 — Implement analyzer-owned generation-safe publication, failure recovery, deletion, and cross-file rebinding/cache invalidation. Done when replay/transport failure preserves manual/other-language facts and unchanged consumers are rebound after declarations/imports/exports change.
- [ ] D08 — Integrate opt-in configuration/schema/templates, `--skip-powershell`, CLI dispatch, exclusions, full/incremental/watch/dry-run behavior, and exit semantics. Done when settings precedence, spaces/non-ASCII paths, skipped inventory, and failed-run cache retention have fixtures.
- [ ] D09 — Publish PowerShell fingerprints/outcomes and audit generic exact/context/impact/connection/snippet queries before enabling capabilities. Done when only proven query combinations become ready and unsupported classes/data flow/Pester shields remain explicit limitations.
- [ ] D10 — Ship worker/parser packaging, Windows/Linux CI and clean-install smoke tests, and `supports.md`. Done when syntax/binding/platform/version limits are documented and first-release phases D01-D09 work from the packaged CLI without globally installed PowerShell.

## E — Close Shared Acceptance And Rollout

Reference: [roadmap and acceptance matrix](references/project-capabilities/2026-10-08-project-capability-roadmap.md).

- [ ] E01 — Run the complete synthetic repository matrix across discovery, indexing, routing, and queries. Done when C# only, TypeScript without tsconfig, Markdown only, Markdown/PowerShell, disabled/enabled SQL, mixed roots, unsupported/empty/inaccessible repositories, and failed/skipped/stale passes have explicit expected outcomes.
- [ ] E02 — Run profile/outcome, constraint/reference, and PowerShell publication/query integration against an isolated Neo4j database. Done when concurrency, replay, deletion, ownership preservation, and failure recovery pass together; existing shared-server temporary fixtures do not close this gate.
- [ ] E03 — Verify fresh-process and packaged-client rollout with persisted caches, old profile/client compatibility, authenticated API/SDK/MCP forwarding, bounded results, schema discovery, and cancellation. Done when the full implemented feature set works outside unit fixtures on Windows/Linux.
- [ ] E04 — Update final feature/CLI/schema/agent/dependency-impact guidance and record release acceptance evidence. Done when documentation matches shipped capabilities, every remaining item is closed or explicitly rescheduled, and any optional external-repository acceptance has its own read-only scope and recorded result.

## Recommended Delivery Order

1. Finish producer identity/content/scope guarantees and current-language coverage in A; these are the main reason implemented code tools still report unknown readiness in production.
2. Refine section requirements and scoped queries in B01-B04 once their evidence contracts are stable.
3. Deliver inventory placement first (C01-C03), then Markdown structure (C04-C09) and B05. Placement does not depend on Markdown parsing or PowerShell indexing.
4. Run D01 early to settle parser feasibility; deliver the PowerShell worker D02-D10 using the shared publication contracts. It can progress independently from C after coordinating CLI/configuration changes.
5. Close E as an integrated release gate, using each workstream's focused tests throughout implementation.

No automatic indexing, dependency installation, source-script execution, or external-repository edits are implied by this checklist. Deferred MVP exclusions in the reference plans remain deferred: arbitrary validators, narrative/canon validation, chapter numbering/front matter, PowerShell classes/enums/data flow, Pester shields, and PSScriptAnalyzer integration.

## Evidence And Verification Baseline

### 2026-10-22: B02 Root And Exact-Target Document Queries

Added a parser-independent `DocumentQueryScope` contract for ordinal inventory identities, directory-descendant boundaries, and exact file targets. Invalid/absolute/traversing paths and targets outside the selected root are rejected before evidence/storage reads. The new scoped full-text storage method requires a project and applies project, root, exact-source, and metadata predicates before the bounded result limit, with deterministic score/ID ordering. Application filters also reject malformed/out-of-scope/cross-project results and recheck each accepted document against current Markdown/text inventory. The public result remains bounded at eight chunks and 600-character content previews.

Document readiness now evaluates only overlapping outcomes for explicit scoped queries. A current successful full outcome for the same scope or a covering parent authorizes the query only when all overlapping evidence matches the analyzer/source identities and remains complete. Unrelated root failures do not block a confined query or contribute readiness. Child outcomes cannot authorize a broader query; the unfiltered fallback still requires project coverage. Inventory generation, exact target, and the same scope requirements are revalidated after execution, including empty queries. Changes discard results, storage failure remains explicit, and cancellation propagates.

MCP `search_documentation` exposes optional `rootPath`/`targetPath`, and a new authenticated REST endpoint plus SDK method forward the same filters. Existing unfiltered calls retain their defaults. Document-review workflow plans and execution pass the exact observed target to storage rather than authorizing a project-wide fallback from root evidence. Composite reports keep their existing project boundary. The dependency catalog and [document-search guidance](../document-search.md) record the new contracts and limits. No new per-root producer aggregation, graph traversal coverage, Markdown validation, or last-good reuse is claimed.

Verification on Windows with existing dependencies: 143 Core, 575 Application, 37 Infrastructure unit, 441 Indexer, and 47 SDK tests, plus 37 focused MCP/API/forwarding/wire tests (1,280 total), passed using `dotnet test <test-project> --no-restore --verbosity minimal` with a filter for the MCP subset. `dotnet build tests/CodeMeridian.Infrastructure.Integration.Tests --no-restore --verbosity minimal` passed with zero warnings/errors, including the new root-before-limit/exact-chunk integration fixture; live Neo4j tests were not run. `git diff --check` passed. CodeMeridian graph queries were skipped at the user's request because recent changes may be unindexed. No repository indexing, dependency installation, deployment, or external-repository edits were performed.

### 2026-10-22: B01 Enabled-Section Report Requirements

Feature-path, changed-subgraph, and responsibility-slice entry points derive requirements from the actual flags, using the same selection for preflight and post-execution revalidation. Disabled document/test sections perform no reads or misleading absence checks. Feature risk remains unknown when tests were not requested. Requested unavailable required sections block before queries. Changed-subgraph requirements now include the namespace, architecture-policy, and history evidence used by its unconditional sections; responsibility namespace planning requires namespace evidence only when enabled.

Responsibility document/community advice and weather bridge analysis have independent readiness and generation checks. Unavailable or stale advisory sections retain explicit reasons while usable required sections remain available. Weather omits unavailable bridge counts and withholds the overall score instead of counting missing evidence as zero; an audited empty bridge query still produces a legitimate zero. Optional failures do not expose backend messages, and cancellation propagates. Context packs revalidate test evidence and rebuild without test-derived data when that evidence changes, recording the omission.

PR-context reports now apply the shared policy to callable, test, and history requirements, adding document coverage only when requested. API/SDK responses carry nullable versioned applicability; the CLI renders unavailable coverage, writes the requested artifact, and returns exit code 2 rather than presenting an empty successful report. Legacy responses without applicability and explicit service construction without profiles retain their prior behavior. Production DI supplies the profile service. Workflow recipes continue using default tool flags. No producer capability, SQL/frontend conformance, root-scoped query support, or placement/reference validation is authorized by this change.

Verification on Windows with existing dependencies: all 542 Application, 441 Indexer, and 44 SDK tests passed, plus 35 focused MCP/API/SDK-forwarding/wire tests (1,062 total), using `dotnet test <test-project> --no-restore --verbosity minimal` with a filter for the MCP subset. New regressions first reproduced disabled flags still blocking reports and weather reading unaudited projection data as zero findings. `git diff --check` passed. CodeMeridian graph queries were skipped at the user's request because recent changes may be unindexed. No repository indexing, dependency installation, deployment, or live Neo4j acceptance was performed.

C03 remains open. Inspection identified missing published filesystem case semantics and lossless policy persistence as prerequisites for a trustworthy public inventory-only placement query; the existing configuration previews remain insufficient.

### 2026-10-22: C01-C02 Contracts, Configuration Validation, And Placement

Added version 1.0 parser-independent group, placement, Markdown-reference policy, exception, diagnostic, and outcome contracts in Core. Strict JSON configuration validation preserves the existing namespace layer/dependency fields and default dependency reasons while rejecting unknown/custom fields, duplicate properties/IDs, unsupported versions/severities/patterns, unknown groups, empty policies, and malformed namespace rules. Optional selectors/collections normalize to empty collections. The configuration indexer validates the active architecture policy before clearing/uploading configuration; invalid or unreadable policies and missing custom paths exit with code 2. The repository loader uses legacy defaults only when configuration is genuinely absent and unconfigured; invalid/empty/missing explicitly configured policy, duplicate/oversized entry sets, and potentially truncated previews fail rather than substitute defaults or execute an all-clear query.

The inventory-only placement evaluator accepts generation-scoped admitted files, discovery state, explicit case sensitivity, known excluded paths, and an optional exact-file target. It evaluates every matching group/rule, applies explicit exceptions before deny/allow checks, normalizes separators/dot segments without lowercasing identities, rejects traversal/collisions/contradictory inventory, and distinguishes missing/excluded/unobserved targets. Partial discovery retains observed findings without claiming complete coverage or absence. Outcomes retain rule identity/severity/reasons, required capabilities, evaluated counts, total findings, bounded ordered samples, and truncation flags. Applicable reference rules remain explicitly unsupported by this evaluator. No file reads or semantic workers are needed for placement.

C01 and C02 are checked. C03 public query/transport/persistence wiring, C04-C09 Markdown evidence/report integration, and B05 capability activation remain open. The new contracts/evaluator do not authorize any new production capability, certify reference resolution, or claim lossless policy persistence through existing 80-character configuration previews. Usage and limits are documented in [repository-constraints.md](../repository-constraints.md).

Verification on Windows with existing dependencies: all 117 Core, 438 Indexer, 32 Infrastructure unit, and 519 Application tests passed using `dotnet test <test-project> --no-restore --verbosity minimal` (1,106 total). Named evidence fixtures are recorded on C01-C02 above. `git diff --check` passed. CodeMeridian graph queries were skipped at the user's request because recent changes may be unindexed. No repository indexing, dependency installation, deployment, or live Neo4j acceptance was performed.

### 2026-10-22: A03 Callable Syntax And Identity Accounting (Partial Delivery)

The C# execution report is now version 1.2. An independent syntax audit compares explicit methods, constructors, local functions, operators, conversions, and indexers with emitted nodes using file, node kind, and exact declaration offset. It also counts unrepresented destructors, accessors (including auto-accessors), expression-bodied properties, anonymous functions, primary constructors, and one entry point per file containing top-level statements. These counts cover successfully extracted files; skipped/read-failed/extraction-failed files retain their existing separate accounting. Compiler-synthesized declarations and inactive preprocessor branches are outside this audit.

The audit compares semantic symbols for declarations sharing a graph identity. Distinct symbols, or missing symbols that cannot establish safe sharing, produce identity-conflict evidence. Fixtures expose generic arity, parameter modifier, explicit interface implementation, and conversion target collisions. Partial method declaration/implementation pairs can legitimately share an identity. This delivery reports existing representation/identity gaps; it does not change graph identities or add callable extraction support for the missing forms.

Unrepresented syntax and identity conflicts produce partial worker coverage and generation-scoped outcomes without changing process exit semantics. Totals persist in the informational `IndexRun` report and flow into terminal outcome limitations. Diagnostic samples share the existing eight-location/code bound; they contain no symbol names, source text, or diagnostic messages. Clean accounting, including zero-candidate inputs, retains unknown coverage. A03 remains unchecked: evaluated project loading/membership, missing representations/identity fixes, full dependency extraction, and per-scope semantic coverage remain open. No new callable capabilities are authorized.

Verification on Windows with existing dependencies: all 138 RoslynIndexer tests and 432 Indexer tests passed using `dotnet test <test-project> --no-restore --verbosity minimal`. Before implementation, new CLI regressions reproduced destructors and colliding generic overloads publishing succeeded/unknown outcomes instead of partial evidence. `CSharpCallableDefinitionAuditTests` covers supported and unrepresented syntax, exact offsets on a shared line, valid zero-candidate input, symbol identity collisions, legitimate cross-file partial methods, ordinary overloads, bounded samples, and cancellation. `CSharpAnalysisReportTests` verifies persisted counts, privacy, failed-file exclusion, and coverage/exit behavior; `CSharpProfilePublicationTests` verifies terminal limitations and outcomes. `git diff --check` passed. CodeMeridian graph queries were skipped at the user's request because recent changes may be unindexed. No repository indexing, dependency installation, deployment, or live Neo4j acceptance was performed.

### 2026-10-22: A03 Semantic Diagnostics And Relationship Accounting (Partial Delivery)

The C# execution report is now version 1.1. It counts synthetic-compilation declaration and method-body errors separately from parse errors, retaining at most eight diagnostic locations/codes across all stages. Location-free diagnostics use `.` with no line number; no diagnostic messages or source text are published. Compilation diagnostics honor cancellation and use the same source-backed compilation as extraction.

The report also includes separate call/type candidate summaries with resolved-local, external/unindexed, unresolved-local, indeterminate, duplicate, synthetic, and unique-edge counts. Semantic errors and unresolved-local/indeterminate candidates produce partial worker and generation-scoped outcome coverage. External targets alone do not imply a local coverage gap, and valid zero-candidate inputs remain explicit. Synthetic compiler errors are analysis limitations rather than evaluated project-build failures, so they do not change the process exit code. Existing parse/read/extraction failure handling remains in force.

The audit fixtures exposed dynamic receivers being classified as external/unindexed. They now remain indeterminate, including when source uses an inferred dynamic receiver or a local method has the same name. This avoids treating runtime dispatch as evidence that a local dependency was absent.

A03 remains unchecked: observed candidate accounting does not prove complete declaration/dependency extraction, and evaluated project membership/loading and per-scope semantic coverage remain open. Clean runs retain unknown semantic coverage. No new callable capabilities are authorized.

Verification on Windows with existing dependencies: all 112 RoslynIndexer tests and 427 Indexer tests passed using `dotnet test <test-project> --no-restore --verbosity minimal`. `CSharpAnalysisReportTests`, `CSharpSemanticDiagnosticsTests`, and `CSharpDynamicCallResolutionTests` cover semantic error counts, parse/semantic separation, bounded/private diagnostics, location-free errors, cancellation, dynamic dispatch, external targets, duplicate accounting, and valid zero-candidate runs. `CSharpProfilePublicationTests` verifies the corresponding terminal outcomes. `git diff --check` passed. CodeMeridian graph queries were skipped at the user's request because recent changes may be unindexed. No repository indexing, dependency installation, deployment, or live Neo4j acceptance was performed.

### 2026-10-21: A03 C# Execution Evidence (Partial Delivery)

The Roslyn worker returns a versioned execution report with requested/read/extracted/failed/skipped file totals, parse-error counts, and up to eight diagnostic locations and codes. It stores that informational report on the existing `IndexRun` diagnostic. The unified CLI forwards the report's totals and limitations into generation-scoped outcomes. Read/extraction/parse failures return exit code 2 and retain the prior cache; intentional generated-file exclusions produce partial coverage while allowing the rest of the index pass to continue. A failed file does not contribute partially extracted nodes or edges. Cancellation and upload failures propagate.

C# constants, syntax extraction, and semantic enrichment now consume a single text read per source file; the unified CLI verifies its exact byte buffer against the captured source snapshot. Transient different-byte reads remain invalid even if source is restored. Empty files and valid zero-dependency files have explicit successful execution evidence without using positive node/edge counts as a completeness test.

A03 remains unchecked: the existing synthetic compilation does not establish evaluated project membership, project-specific parsing/compilation settings, or complete callable definitions/dependencies. Successful runs retain unknown semantic coverage, and the current outcome scope remains conservative at project scope. Root membership/aggregation remains A12. This slice does not authorize new callable queries or claim live persistence acceptance.

Verification: all 424 Indexer and 101 RoslynIndexer tests passed. `CSharpProfilePublicationTests` covers full/delta/zero-edge/parse/extraction/skipped/missing-report outcomes, source restoration, and upload failure. `CSharpAnalysisReportTests` covers missing inputs, bounded diagnostics, single-read parsing, failed-file isolation, generated-file exclusions, cancellation, and upload failure. No repository indexing, dependency installation, source-script execution, deployment, or live Neo4j acceptance was performed.

### 2026-10-20: A01 Worker Identities

Node workers now expose a bounded, versioned `--describe-worker` protocol before repository configuration or source discovery. Reports identify the executing Node runtime, resolved parser packages and their runtime file hashes, the compiler bundled by ts-morph, and the PostgreSQL grammar loaded by the WASM parser. Worker/shared implementation bytes and resolved runtime identities feed the existing implementation/parser fingerprint contract. Discovery probes do not restore dependencies, index source files, or contact the backend.

Normal index publication and cache reconciliation probe the same worker assets used for execution. Missing, malformed, unavailable, timed-out, and incompatible reports leave identity unknown; matching null identities cannot authorize cache reuse. Workers check the published expected identity before analysis and their startup identity again after analysis. SQL's separate cache includes the reported worker fingerprint. The standalone local `profile` command remains discovery-only and does not invent worker fingerprints. Worker identities alone do not establish audited semantic coverage or bind analyzed repository source content; source binding is delivered separately below and A03-A12 remain open.

### 2026-10-20: A02 Source Binding

Normal indexing now captures a versioned SHA-256 identity over the complete accepted inventory's sorted paths, kinds, and raw file hashes. Profiles and generation-scoped outcomes carry this optional `SourceFingerprint`; storage rejects mismatched bindings, and routing rejects missing or mismatched source identities. Older 1.0 payloads remain readable and valid uploads, with publication retry hashes preserved, but unbound outcomes cannot establish readiness and unbound profiles cannot authorize cache reuse. Standalone local/published `profile` remains discovery-only.

The CLI re-enumerates and hashes inputs around each analyzer and before terminal publication. All terminal outcomes wait until the final check, so a later source change also invalidates earlier completed analyzers. Changed/added/deleted/unreadable inputs fail the pass without advancing the CLI cache. A change detected after terminal upload reserves a new generation, making the uploaded evidence stale. Document parsing and local MCP-tool reference extraction consume text decoded from the exact byte buffer checked against the captured hash; a transient mismatching read remains invalid even if the file is restored. Missing document reads cannot claim complete coverage or advance the cache.

Binding is deliberately conservative across the whole accepted inventory; unrelated accepted-file changes also invalidate the pass. Other workers carry the captured snapshot identity with unknown coverage. Their actual source-read reports and semantic coverage still require the A03-A10 audits before complete coverage can be enabled. This does not implement A11 last-good reuse or A12 root membership/aggregation.

Verification: 415 Indexer, 519 Application, 68 Core, 22 DocumentIndexer, 25 Infrastructure unit tests, and 29 focused MCP/API/forwarding/wire tests passed. The Neo4j integration project builds with updated source-binding persistence/conflict fixtures; those live database fixtures were not run. No repository indexing or deployment was performed.

Verification on Windows with existing dependencies: `npm run build`; all 390 Indexer tests; 15 shared-worker tests (including fresh source/compiled worker processes); 92 TypeScript tests; 7 frontend tests; and 14 SQL tests passed. No repository indexing, dependency installation, or live Neo4j acceptance was performed.

### Earlier Baseline

Reviewed on 2026-10-19: the five source plans, current uncommitted changes, profile/outcome publication seams, tool policy/guard implementation, `LocalProjectProfileBuilder`, `ProjectAnalysisFingerprints`, `IndexProfileRun`, `ArchitectureRuleSet`, and `DocumentReferenceExtractor`. The latter still uses regex; architecture contracts still describe namespace layers/dependencies; the profile builder explicitly marks PowerShell unsupported. No dedicated PowerShell worker or repository-constraint query was found in the reviewed source surfaces.

CodeMeridian's live profile returned unknown, and no `ProjectProfile` graph nodes were found. Indexed documentation contains both old plan snapshots and newer implementation notes. Local source and the working-tree plans control this baseline; graph absence is not proof of feature absence.

Previously recorded verification on 2026-10-16: 511 Application tests, 364 Indexer tests, and 74 focused MCP/API/schema/forwarding tests passed. These counts describe that implementation snapshot, not fresh October 19 runtime acceptance or complete producer coverage. The routing batch did not run live Neo4j acceptance.

## Reference Plans

- [Roadmap](references/project-capabilities/2026-10-08-project-capability-roadmap.md)
- [Discovery and capability profiles](references/project-capabilities/2026-10-08-project-discovery-capability-profile-plan.md)
- [Capability-aware tool routing](references/project-capabilities/2026-10-08-capability-aware-tool-routing-plan.md)
- [Repository constraints and Markdown](references/project-capabilities/2026-10-08-repository-constraints-markdown-plan.md)
- [Offline PowerShell indexing](references/project-capabilities/2026-10-08-powershell-indexing-plan.md)
