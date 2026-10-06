# Cross-Solution Package And Source References

Date: 2026-10-06

Status: implemented in the working tree; fixture acceptance and packaged CLI verification completed. Shared runtime deployment is not part of this change.

## Outcome And Recommendation

When one indexed solution publishes a library and another consumes its NuGet package, developers should be able to navigate from a consumer type or call site to the library's indexed source, inspect cross-project dependencies, and find affected consumers before changing the library.

Use compiler-bound symbol identity plus package/assembly provenance. Preserve the consumer's actual compiled dependency separately from its link to indexed source. Reconcile links on the server after successful index publication, in either indexing order.

Default to showing an unambiguous current-source association when the published revision is unavailable, with an explicit version/provenance status. Only verified source matches participate in normal source call and impact traversal. Current-source associations remain available in cross-project dependency results and navigation without being presented as the implementation of the installed binary.

The user authorized implementation of this plan. The chosen default balances useful navigation with trustworthy impact results. Namespace-only matching is insufficient to create a source relationship.

## Implementation And Acceptance

Implemented per-project/per-framework `MSBuildWorkspace` evaluation behind the existing repository trust flag; versioned Core records; SDK/API begin and completed-snapshot publication; indexed Neo4j exports and reference sites; Application matching; persisted reconciliation; source-change/deletion invalidation; and isolated provenance-bearing edges. Cross-project dependency results have structured and Markdown output, current-source consumers appear separately in impact results, and `doctor` reports resolution counts. Type-forwarding origin hints retain facade packages while matching the defining assembly.

Acceptance covered local NuGet packing and both repository indexing orders, overloads, constructors and interfaces, canonical symbol round trips, conditional multi-target declarations, external source project references, revision/version differences, ambiguous ownership, retries, superseded generations, source changes and deletion, preservation of manual edges, and normal connection/impact/editing traversal. The packaged CLI was installed into a temporary tool directory and exercised against an isolated HTTP server and Neo4j database; consumer-first indexing produced five unresolved sites which linked after producer indexing. An unchanged index retried reconciliation successfully.

Evaluated configuration is currently Debug. Missing SDK/assets or compiler/evaluation failures degrade provenance and leave syntax indexing available. Package repository metadata and a clean matching commit are the supported exact-provenance inputs; historical source checkout, custom pipeline manifest ingestion and reproducible-binary validation remain optional future enrichment. No shared graph was cleared or deployed. Usage and bindings are documented in [indexing.md](../indexing.md#cross-solution-net-package-references).

Feature policy, contracts and package storage live in small dedicated files. Existing large query/repository orchestration files receive only integration points; splitting those established surfaces would add unrelated refactoring to this feature.

## Assumptions And Scope

- Producer and consumer are independently indexed into the same CodeMeridian graph, using distinct project contexts.
- A project context identifies an indexed repository/solution; a build project identifies an individual `.csproj`. Keep these concepts separate.
- Initial delivery supports .NET NuGet dependencies and explicit source `ProjectReference`s, including dependencies outside the repository root.
- Consumers have restored assets available. Indexing does not automatically restore packages, build projects, download source, or change either repository's pipeline.
- Work without evaluated metadata continues to use existing syntax indexing and reports its limitations; it cannot claim verified cross-solution relationships.
- Producer pipeline changes are optional enrichment for exact release provenance, not a requirement for discovering an associated current source project.
- No npm support, network-service tracing, package feed crawler, automatic checkout, or historical source storage in this feature.

## Current Evidence

Graph lookup identified existing cross-project reporting. Source inspection confirmed the missing extraction and reconciliation path; graph feature matches do not establish that this feature is implemented.

| Surface | Current behavior | Consequence |
|---|---|---|
| `tools/RoslynIndexer/Pipeline/CSharpProjectScope.cs` | Follows `ProjectReference` and `.slnx` paths into one index run | Can discover source outside the root, but does not establish a separately indexed producer identity |
| `CSharpSemanticProjectInputs.cs` | Reads NuGet compile assemblies from restored `obj/project.assets.json` | Package binaries are available for semantic binding, but package-to-source identity is not retained |
| `CSharpSemanticModelCatalog.cs` | Creates one synthetic compilation and deduplicates reference assemblies by filename | Does not safely distinguish build projects, framework variants, or different assemblies with the same filename |
| `CSharpInvocationEvidenceExtractor.cs` and `CSharpAstWalker.cs` | Capture semantic declaring-type and declaration-location hints | Missing a durable external symbol and assembly/package identity |
| `CSharpCallEdgeResolver.cs` | Drops compiler-bound metadata calls as external/unindexed | Separately indexed source cannot recover those calls later |
| `CSharpReferenceEdgeResolver.cs` | Resolves against the current run's node catalog | No cross-context source matching for external types |
| `src/Infrastructure/Graph/Neo4jCodeGraphRepository.Analytics.cs` | Reports existing structural edges across contexts | Query support exists; automatic package/source linking does not |
| `src/McpServer/Api/KnowledgeApiEndpoints.cs` | Provides node/edge ingestion, without a package-reference publication contract | Reconciliation needs an explicit completed-index boundary |

The existing cross-project integration test inserts an edge directly. It is not evidence of an end-to-end NuGet link. Existing relationship-health accounting must remain intact; expected unresolved external dependencies must not reduce local confidence.

## Developer Experience

1. Index the library repository and application repository in either order with the normal index command.
2. Detect packages and assembly identities from locally available restore/build metadata; require no hand-maintained namespace map for ordinary cases.
3. Report dependency origin, package/version, target framework, consumer call site, linked source location, producer project, and resolution status.
4. Show actionable statuses: verified source, associated current source, ambiguous producer, source not indexed, or metadata unavailable.
5. If the installed package is v1 and the library checkout is v2, retain the v1 compiled dependency and label the v2 source association. Never label it a v1 implementation.
6. If several producer repositories qualify, show bounded candidates and allow an explicit package-to-producer binding. A binding selects a producer; it does not prove revision equivalence.
7. Keep ordinary framework dependencies as compact external summaries. Do not flood results with framework symbol stubs.

## Identity And Metadata Model

### Build scopes and producer exports

Add a versioned build-scope manifest containing project context, normalized repository-relative project path, target framework, configuration, assembly identity, package identity when known, indexed commit, dirty-worktree status, and metadata provenance.

Persist producer export mappings from canonical symbols to existing source nodes, scoped to the build manifest. Do not overwrite an existing source node's properties with whichever target framework is indexed last. If existing canonical IDs merge distinct declarations, retain distinct export identities and mark source mapping ambiguous until it can be represented safely; never choose the first node.

Use evaluated project/build information for `AssemblyName`, `PackageId`, package version, compilation inputs, conditional symbols, imports, and framework references. Reading literal `.csproj` values alone cannot reliably handle `Directory.Build.props`, central package management, conditions, or CI version injection. Do not equate NuGet version, assembly version, and repository commit.

### Compiler symbol keys

Use a versioned, language-neutral identity contract built from Roslyn original definitions and a canonical declaration identifier, such as documentation-comment IDs, with explicit assembly ownership. Confirm the chosen serialization through round-trip fixture tests before freezing it.

Cover nested and generic types, method generic arity, overload parameter types, arrays, nullable/value types, `ref`/`out`/`in`, constructors, extension methods through `ReducedFrom`, and explicit interface implementations. Include additional signature discriminators where the base identifier cannot distinguish relevant metadata. Exclude Roslyn's in-memory `SymbolKey` as the durable public contract.

Keep symbol identity case-sensitive. Normalize paths and package identity according to their own rules. Never use namespace, short type name, argument count, or assembly filename alone as a unique key.

### Consumer references

Preserve a deduplicated external-symbol record owned by the consumer build scope, with package/version, selected compile asset, full assembly identity, canonical symbol key, relationship kind, and bounded call-site evidence. Keep repeated call sites available independently of the dependency edge deduplication policy.

Represent the original dependency with a stable reference node/record even when producer source is absent. Separate its source association and resolution status from the original dependency. Avoid manufacturing a source node under the consumer's project context.

Capture semantic type references for `Uses`, `Inherits`, and `Implements`, in addition to method calls and constructor calls. A `using` directive is an import hint, not proof that code uses a particular package or type.

## Matching And Version Policy

Match in this order:

1. Resolve the consumer's bound symbol and assembly to its selected dependency asset or source project.
2. Find producer exports with the same assembly ownership and canonical symbol identity, using available package/repository identity to constrain candidates.
3. Confirm build/framework compatibility and that exactly one producer/export qualifies. Type forwarding must resolve to the defining assembly while retaining the facade/package origin.
4. Determine source provenance separately from symbol compatibility.
5. Persist the status, evidence, and reason; never silently break ties using indexing order or timestamps.

| Status | Requirements | Default behavior |
|---|---|---|
| Verified source | Unique symbol/producer and evidence tying the consumed artifact to the indexed source revision/build scope; indexed working tree matches that evidence | Materialize a provenance-bearing source relationship for normal connection and impact traversal |
| Associated current source | Unique producer and compatible symbol, but release/source equivalence is missing, unknown, or different | Show navigation and cross-project association with explicit source/version status; exclude from normal source call traversal |
| Ambiguous | Multiple candidates, colliding export mappings, or conflicting provenance | Preserve dependency and show bounded candidates; create no source edge |
| Source not indexed | Binary dependency known, no eligible source export | Preserve external dependency; link automatically when a producer is later published |
| Metadata unavailable | No trustworthy binding or asset/build identity | Preserve available hints and explain the missing prerequisite; no automatic source edge |

Matching package versions alone do not prove that a checkout contains the released implementation. A clean matching commit plus package repository metadata or a pipeline-produced artifact-to-source manifest can establish provenance under the indexer's trust model; binary/source reproducibility validation is a stronger optional check.

Source Link and NuGet repository metadata are useful provenance sources, not prerequisites for current-source association. Source Link supports revision-specific source mapping, and Microsoft recommends repository commit metadata and deterministic builds for traceability. See [Microsoft's Source Link guidance](https://learn.microsoft.com/dotnet/standard/library-guidance/sourcelink) and the [Source Link project](https://github.com/dotnet/sourcelink).

Allow explicit producer bindings only for ambiguous ownership. Define the configuration through existing `meridian.json` conventions after checking configuration definitions and usage. Do not introduce a general matching rule language.

## Architecture And Publication Lifecycle

- Core owns reference/export identity, statuses, and repository contracts, without Roslyn, MSBuild, Neo4j, or MCP dependencies.
- Indexer owns compiler binding and locally available build/package metadata extraction.
- Application owns deterministic matching policy and orchestration.
- Infrastructure owns indexed candidate lookup, manifests, atomic publication, reconciliation writes, and all Cypher.
- SDK/API carry versioned ingestion and completion contracts. MCP exposes facts and bounded evidence through existing tools where practical.

Use an explicit indexing generation and completion operation. Stage manifests, exports, and external-reference observations; publish only a complete scope snapshot. Existing live state remains usable during staging. Do not reconcile after each node batch.

After publication, reconcile the changed consumer's references and consumers affected by changed producer export keys. Use indexed package/assembly/symbol lookups, bounded batches, cancellation, and idempotent writes rather than scanning every project. Persist unfinished reconciliation state for safe retry if publication succeeds but linking fails.

Derived source edges carry resolver, generation, source-association identity, and evidence. Reconciliation replaces only feature-owned derived edges. Preserve manually ingested and existing local edges, including when they share endpoints and relationship kinds; define merge/provenance handling before implementing writes.

Producer deletion removes its exports and derived source links, while surviving consumer dependency records remain unresolved. Consumer deletion removes its observations and derived edges. File deletion, changed package versions, renamed symbols, framework changes, full clear, and producer reindexing must invalidate the appropriate mappings. Repeating an unchanged publication must not change edge counts.

## Implementation Sequence And Verification Gates

### 1. Reproduce the missing relationship

- Create isolated producer and consumer fixture solutions with a locally packed NuGet package; use a local temporary feed, not a public feed.
- Give each repository its own project context. Index in both orders and assert the current missing source relationship.
- Add collision, overload, and version-difference fixtures before changing resolution.

Gate: tests distinguish preexisting manually inserted cross-project edges from actual extracted package references.

### 2. Establish trustworthy build scopes

- Replace the single synthetic semantic catalog for the feature path with per-project/per-framework compilations using evaluated inputs.
- Recommend `MSBuildWorkspace` for trusted repository evaluation, respecting the existing repo-script trust policy because design-time evaluation may run repository build logic. Reuse evaluated manifests when available; do not silently invoke restore or build.
- Keep a read-only syntax fallback. Report incomplete evaluation, missing SDK/assets, project ambiguity, and unsupported configurations explicitly.
- Retain provenance of each selected reference asset and package, including transitive packages and source `ProjectReference`s.

Roslyn's [MSBuildWorkspace implementation](https://github.com/dotnet/roslyn/blob/main/src/Workspaces/MSBuild/Core/MSBuild/MSBuildWorkspace.cs) provides project loading and compilation integration. The choice to use it here is a recommendation; validate tool packaging and trust behavior with fixtures before adopting it.

Gate: two projects with identical filenames or different versions cannot contaminate each other's semantic binding; multi-target fixtures select the correct assets.

### 3. Publish exports and preserve external references

- Add the versioned identity/manifests and producer export mapping.
- Enrich invocation/type extraction with compiler-bound identity and asset origin.
- Preserve externally bound candidates instead of discarding them in the local resolvers.
- Extend SDK/API ingestion and scope-completion contracts additively; older clients remain supported without automatic cross-project linking.
- Keep local candidate accounting unchanged; record cross-project resolution statistics separately.

Gate: producer absent still yields a usable dependency record; overloaded and generic symbols round-trip correctly; ordinary local resolution remains unchanged.

### 4. Reconcile and maintain derived relationships

- Add indexed repository candidate lookup and deterministic Application matching.
- Implement successful-generation publication, targeted reconciliation, retry, and deletion/reindex cleanup.
- Create normal source relationships only for verified mappings. Persist weaker associations separately with reasons and version evidence.

Gate: both indexing orders, retries, concurrent publications, interrupted staging, deletions, and unchanged reindexing produce consistent results without duplicate or stale edges.

### 5. Expose useful developer results

- Extend `find_cross_project_dependencies` to report compiled dependency and source-association status, including external interface inheritance/implementation where relevant.
- Include verified cross-project edges in `find_connection`, `find_impact`, `get_context_for_editing`, and `build_minimal_context`.
- Show associated-current-source consumers separately as potential impact, with an explanation that their deployed implementation is not verified against the checkout. Do not hide this useful signal entirely.
- Add package/version, producer, source location, framework, provenance, and resolution reason to structured and Markdown results from the same protocol-neutral DTOs.
- Support navigation/potential impact for weaker associations through explicit query options or a bounded association section; no new suite of tools unless existing interfaces cannot carry the facts clearly.
- Report unresolved/ambiguous association totals in index summaries and `doctor` without relabeling them as local relationship failures.

Gate: a developer can answer which package is consumed, where its related source lives, and which consumers may need attention without mistaking a newer checkout for the installed implementation.

### 6. Document and release

- Update `docs/indexing.md`, `docs/features.md`, CLI help, and configuration schema where changed.
- Document two independent repositories/pipelines, indexing in either order, optional producer provenance enrichment, version differences, trust requirements, and degraded operation.
- Run focused tests at each gate, then the required .NET and isolated Neo4j checks for the final implementation.
- Validate against two real indexed solutions only after fixture acceptance; do not deploy or clear shared graph data as part of this plan.

Gate: published documentation and discovery/structured contracts agree with observed behavior.

## Likely Implementation Surface

These are source-inspected starting points, not permission to edit all files. Resolve exact symbols, refresh graph context, and inspect impact/test shields before implementation.

| Responsibility | Existing starting points |
|---|---|
| Project/build scopes | `tools/RoslynIndexer/Pipeline/CSharpProjectScope.cs`, `CSharpSemanticProjectInputs.cs`, `CSharpSemanticModelCatalog.cs` |
| Symbol extraction | `CSharpInvocationEvidenceExtractor.cs`, `CSharpAstWalker.cs`, `CSharpIngestModels.cs` |
| External preservation and publication | `CSharpCallEdgeResolver.cs`, `CSharpReferenceEdgeResolver.cs`, `CSharpIndexer.cs`, `CSharpBatchIngestionWriter.cs` |
| Index completion coordination | `tools/Indexer/Cli/IndexCommandHandler.cs` and the existing orchestration it invokes |
| Contracts | `src/Core/CodeGraph/`, `src/Sdk/CodeMeridianClient.cs`, `src/McpServer/Api/KnowledgeApiEndpoints.cs` |
| Matching | New focused Application service and Core reference/export records |
| Storage and traversal | Focused partials under `src/Infrastructure/Graph/`; existing analytics and connection/impact queries |
| Output | `src/Application/Services/CodebaseQueryService.Analytics.cs` and relevant context/result formatters; `src/McpServer/Tools/CodebaseTools.Analytics.cs` |

Keep one public type per file and new files preferably under 300 lines. Avoid a broad repository or query-service refactor.

## Test Matrix

| Scenario | Required assertion |
|---|---|
| Consumer first / producer first | Same resolved associations after both completed runs |
| Verified package release | Call/type relationship reaches the correct producer source and impact reaches the consumer |
| Same package version, unknown revision or dirty checkout | Association remains unverified |
| v1 consumer / v2 source | Installed v1 retained; v2 association visibly marked; no false verified call edge |
| Removed method or changed overload | Dependency remains; incompatible source mapping is removed |
| Namespace, assembly filename, or type-name collision | No first-match linking; candidates/status explain ambiguity |
| Multiple builds/frameworks | Correct scope/asset; no property overwrites or cross-framework contamination |
| Extension/generic/nested/explicit-interface symbols | Original defining symbol matched without overload collapse |
| Type forwarding and transitive package | Defining assembly and originating package both preserved |
| Source project outside root | Ownership connects to independently indexed source without duplicating it as consumer-owned code |
| Producer absent, missing assets, evaluation failure | Useful unresolved status, existing syntax indexing preserved |
| Retry, interrupted indexing, concurrent runs | Only completed generations published; no duplicates or lost reference records |
| Consumer/producer/file deletion and reindex | Stale derived links removed; unrelated and manual edges preserved |
| Existing clients and tool contracts | Additive ingestion support; structured/text outputs agree |
| Large dependency sets | Indexed lookups and bounded reconciliation; no all-project scan per reference |

Use xUnit, FluentAssertions, and NSubstitute for isolated policy/orchestration tests. Use the existing isolated Neo4j integration harness for lifecycle and actual Cypher behavior. Add an end-to-end local-feed fixture across two repositories, not just tests that manually insert an edge. Record representative fixture sizes and reconciliation time before setting a performance budget.

## Acceptance Criteria

- Two independently indexed .NET solutions connect automatically through a NuGet dependency without a namespace mapping in the ordinary unambiguous case.
- Consumer calls, constructors, and type relationships preserve compiled identity and find the correct producer export when evidence permits.
- Namespace coincidence, ambiguous ownership, or missing provenance never produces a verified source edge.
- Verified matches work through existing connection, impact, editing-context, and cross-project queries.
- Current-source associations remain useful and clearly distinguish potential consumers from verified source callers.
- Different indexing order, repeated indexing, deletion, and failed publication do not leave incorrect derived edges.
- No automatic package restore, source checkout, or build occurs outside existing repository trust behavior.
- Older clients and existing local relationship-health semantics remain compatible.

## Alternatives And Limits

Namespace matching is simpler but cannot distinguish unrelated packages or overloaded members. Reject it as an authoritative resolver; retain it only as a diagnostic candidate hint.

Requiring a matching historical source checkout for every link offers stronger release accuracy but often leaves useful developer navigation unavailable. Recommend visible current-source associations while reserving normal source traversal for verified matches.

Resolving only inside the consumer indexer would avoid a server reconciliation service, but makes results dependent on indexing order and leaves stale links after producer-only changes. Server reconciliation is justified by independently operated repositories and pipelines.

Do not claim that a source match predicts binary compatibility, deployment state, or runtime dispatch. Interface links identify the contract; runtime implementation selection remains outside this feature. If historical release snapshots are needed later, design them separately instead of adding versioned source storage now.
