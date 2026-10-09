# Project Discovery And Capability Profiles

Date: 2026-10-08

Status: in progress; local discovery, persisted inventory/generations, and authenticated API/SDK/MCP exposure implemented. Analyzer outcome tracking and routing remain planned.

Dependency: foundation for the [roadmap](2026-10-08-project-capability-roadmap.md).

## Implemented Slice

`codemeridian profile [path] [--project name] [--format text|json]` provides local discovery without contacting the backend or running scripts. Core contains versioned profile DTOs; Tooling enumerates accepted files without following reparse points; the CLI classifies files using existing indexing predicates and configuration matching.

Observed files remain visible when analyzer skip flags are set. Indexing readiness stays unknown because no server evidence is queried. Counts remain complete while example paths, root records, and warnings are bounded. Linked/inaccessible paths produce partial discovery and exit code 2. Root records describe observed manifest/source evidence and do not claim evaluated project membership.

Automated fixtures cover composition, exclusions, unsupported PowerShell, disabled SQL, skip flags, bounded output, paths, cancellation, JSON, configuration precedence, and existing CLI commands. Read-only acceptance on `ava-sporelight` found Markdown and PowerShell separately. A Windows junction smoke check verified that an external C# file was not traversed.

`profile --publish` now reserves a server-issued generation before discovery and publishes complete inventories atomically. Neo4j stores project-scoped inventory independently of code nodes. Identical retries are idempotent; older/conflicting published generations are rejected. Partial discovery retains the previous complete profile. The API, SDK, and structured MCP `get_project_profile` expose bounded summaries and target lookup. Missing profiles report unknown; reserved unpublished generations report stale. Semantic indexing readiness remains unknown.

Core validation, application status handling, CLI publication order/full inventory, SDK forwarding, authenticated API/MCP contracts, and Neo4j persistence/concurrency/deletion tests cover this slice. The Neo4j test uses a unique temporary project on the configured database and removes its fixture records; it does not claim isolated-database acceptance.

Remaining work: revision and parser fingerprints, per-root analyzer outcomes, automatic publication from index commands, and capability-aware routing integration.

## Problem And Intended Behavior

The CLI discovers source types to dispatch indexers, but that information is not a complete persisted contract used by query tools. An empty graph cannot distinguish a Markdown-only project from disabled, unsupported, failed, or stale code indexing.

Add a bounded project profile that describes observed repository contents and available analysis evidence. Proposed public query: `get_project_profile(projectContext, targetPath?)`. Expose the same facts through the authenticated API/SDK and a CLI profile command following existing conventions.

## Scope And Assumptions

- Profile code, documentation, configuration, SQL, PowerShell, assets, and unrecognized files without requiring every file type to have a semantic indexer.
- Detect content independently from skip flags and opt-in analyzer configuration.
- Respect the existing exclusion policy, repository boundary, and local/global configuration precedence.
- Distinguish excluded paths from unknown or inaccessible paths; incomplete enumeration cannot establish absence.
- Record observed analysis roots without guessing that a folder is a separate project.
- Do not infer business domain, story canon, or architectural style from file counts.
- Do not add a plugin framework, arbitrary detector scripting, or per-language MCP tool copies.

## Proposed Contract

Keep parser-independent profile types in Core, one public type per file. Suggested fields:

| Field | Meaning |
|---|---|
| contractVersion, project, revision | Stable contract and project identity; repository revision when available |
| observedAt, publishedAt, generation | Discovery provenance and successfully published generation |
| completeness, warnings | Complete/partial/unknown discovery with bounded reasons |
| roots | Repository-relative analysis roots and membership rules |
| fileKinds | Counts and bounded example paths for observed source/document/asset types |
| analyzerSupport | Supported capabilities and parser/indexer version fingerprints |
| configurationState | Enabled/disabled/skipped analyzer state; no secrets |
| indexingState | Not run, running, succeeded, partial, failed, or stale for each scope |
| capabilities | Capability IDs, scope, evidence, freshness, and limitations |
| architecture | Configured rule families and their applicability; no inferred clean result |

Capability IDs describe evidence such as document search, file inventory, callable definitions, callable dependencies, SQL object dependencies, or Markdown references. C# and TypeScript can satisfy the same capability. A TypeScript source file is not proof of frontend cascade evidence.

Expose aggregate counts and bounded examples by default. A separately bounded target lookup can consult the persisted inventory without dumping every path into context.

## Discovery And Publication

1. Enumerate accepted repository files once where possible, sharing existing ignore/path logic.
2. Collect file-kind facts and source-root evidence before selecting enabled indexers.
3. Build an immutable discovery snapshot with partial-enumeration warnings.
4. Track per-analyzer outcomes separately; do not label a started or failed pass successful.
5. Publish versioned inventory/profile facts through the existing authenticated client boundary.
6. Replace the project inventory atomically for a completed enumeration; retain the last successful inventory on discovery/publication failure.
7. Publish analyzer outcomes against the matching inventory generation. Failed analysis can retain last-good facts but must mark them stale or incomplete.
8. Recompute capabilities from reported support plus successful evidence. Prevent late writes from older runs overwriting newer generations.

A skipped analyzer must not erase observed files or imply unsupported syntax. A successful analyzer finding zero call edges can still have valid definitions; record completeness instead of requiring a positive edge count.

The remote service reads uploaded profiles. A local CLI discovery command can work before indexing, but its result must say it is local discovery and has no uploaded semantic evidence.

## Existing And Proposed Surfaces

| Surface | Work |
|---|---|
| `src/Tooling/Discovery/ProjectDiscoveryService.cs`, `IProjectDiscoveryService.cs` | Reuse detection and traversal seams |
| `tools/Indexer/Cli/IndexCommandHandler.cs`, `IndexExecutionPlanBuilder.cs` | Separate observed contents from configured execution; publish outcomes |
| `src/Tooling/Storage/IncrementalIndexCache.cs` | Track generation/fingerprints without treating cache presence as successful server publication |
| `src/Tooling/Configuration/`, `meridian.schema.json` | Capability overrides only if needed; validate existing configuration semantics |
| Core, Application, Infrastructure, SDK, MCP/API | Proposed profile contracts, publication/read ports, storage, forwarding, and exposure |
| CLI `PrintCapabilities()` | Keep product support distinct from project readiness; reuse terminology |

The graph linked discovery to `IndexerDiscoveryTests` and the planner to its existing test suites. Broad feature matches pointing to diagnostics are contextual, not intended primary edit targets.

## Phases

1. Define profile/capability IDs, completeness semantics, scope membership, and compatibility tests.
2. Implement discovery and local profile output for current file types, including unsupported PowerShell.
3. Add generation-safe inventory/profile publication and bounded query exposure.
4. Connect analyzer results, incremental/deletion behavior, and stale-profile handling.
5. Update CLI/API/MCP documentation and ship the shared acceptance fixtures.

## Verification And Acceptance

- Fixtures: C# only, TypeScript without tsconfig, Markdown only, Markdown plus PowerShell, SQL disabled, mixed roots, unsupported files, empty repository.
- Verify exclusions, inaccessible directories, case-sensitive paths, normalized separators, spaces, non-ASCII names, and symlink traversal boundaries.
- Verify disabled/skipped analyzers do not change detected inventory.
- Verify partial indexing, network failure, concurrent generations, and deleted files preserve accurate readiness.
- Verify profile publication survives a fresh process and API queries remain project-scoped.
- Verify auth, SDK forwarding, bounded responses, cancellation, and MCP schema/discovery.
- A missing profile returns unknown/not indexed, never documentation-only.
- Read-only `ava-sporelight` acceptance reports Markdown and PowerShell files separately without running scripts.

Start with `IndexerDiscoveryTests`, `IndexExecutionPlanBuilderTests`, `IndexCommandHandlerTests`, and `IncrementalIndexCacheTests`; add focused Core/API/persistence tests. Run integration tests against an isolated Neo4j database.

## Risks And Rollout

Large inventories require batching and bounded reads. Shared-server profiles need project scoping and revision warnings. Old clients/graphs lack profiles: return explicit unknown readiness and preserve manual discovery workflows; do not invent capabilities. No raw config values, script bodies, or credentials belong in the profile.
