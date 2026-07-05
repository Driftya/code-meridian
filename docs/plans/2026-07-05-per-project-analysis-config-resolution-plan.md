# Per-Project Analysis Configuration Resolution Plan

- Status: implemented
- Date: 2026-07-05
- Scope: make live MCP analysis behavior resolve `analysis` settings in the same conceptual order as the CLI config flow: shared global `meridian.json` first, then project-local `meridian.json`
- Principle: runtime tool behavior should follow the effective analysis configuration for the requested project without requiring a separate MCP server per repository or runtime-folder config rewrites

## Problem Statement

Current behavior is split across two different config models:

- The CLI and indexer already understand both project-local and global `meridian.json`.
- The MCP server currently binds `CodebaseAnalysisOptions` once from server runtime configuration and then keeps that singleton for live tool execution.
- Project-local `meridian.json` is indexed into the graph, but live tool execution does not currently rehydrate those indexed `analysis.*` values.
- User-level global `meridian.json` is a real config source for the CLI today, but live analysis resolution does not explicitly model it as the shared baseline for per-project analysis behavior.

That means the new analysis and noise-reduction settings are configurable in `meridian.json`, but live MCP behavior still does not fully follow the repo config model users already have for `codemeridian init`, `codemeridian index`, and `codemeridian doctor`.

## Verified Current Behavior

- `ToolConfigurationService` resolves CLI config using environment values, then project-local `meridian.json`, then global `meridian.json`.
- `IndexCommandSettingsFactory` already treats local config as overriding global config for indexing concerns such as `configurationFiles`, architecture path, file roles, repo scripts, and cache mode.
- `CodeMeridianConfigFileStore` can load:
  - project-local `meridian.json`
  - global `%LOCALAPPDATA%\\CodeMeridian\\meridian.json`
  - an override global directory via `CODEMERIDIAN_CONFIG_HOME`
- `Application.DependencyInjection` binds `CodebaseAnalysisOptions` from runtime configuration sections `analysis` and `CodeMeridian:Analysis`.
- `CodebaseQueryService` currently captures one `CodebaseAnalysisOptions` instance at construction time and uses it for all projects.
- Project `meridian.json` is indexed today as configuration graph data, including `ConfigurationFile` and `ConfigurationEntry` nodes.
- There is no current path that resolves effective analysis options as:
  - shared global `meridian.json`
  - then project-local indexed `meridian.json`
  - then request-specific overrides

## Goal

When a tool runs against project `X`, CodeMeridian should resolve effective analysis settings in two stages:

1. Build a shared baseline from existing runtime-bound options plus global `meridian.json` `analysis.*` when present.
2. Overlay project-local indexed `meridian.json` `analysis.*` for project `X`.

Project-local config must win over global config. Explicit per-call/tool overrides must still win over both.

## Implementation Outcome

- [x] Added `IProjectAnalysisOptionsResolver` in `Application` to merge runtime defaults, global analysis entries, and project-local indexed analysis entries.
- [x] Added MCP-host `GlobalMeridianAnalysisConfigurationSource` so live analysis reads global `%LOCALAPPDATA%\\CodeMeridian\\meridian.json` or `CODEMERIDIAN_CONFIG_HOME\\meridian.json` first.
- [x] Updated `CodebaseQueryService` to resolve effective analysis options per request/project instead of capturing one singleton `CodebaseAnalysisOptions` for all projects.
- [x] Scoped project-local overrides to exact indexed file path `meridian.json` and `analysis:*` configuration entries.
- [x] Implemented partial merge semantics where scalar values replace lower layers and explicit list values replace lower-layer lists.
- [x] Added focused regression coverage for:
  - global plus local precedence
  - invalid partial overrides not discarding sibling valid values
  - list replacement behavior
  - project-local key flattening for nested analysis arrays/objects
  - global analysis file loading from the configured global home
- [x] Updated indexing/global-config docs to document live analysis precedence and the required re-index after repo-local `analysis` changes.

## Non-Goals

- Do not make indexed project config drive bootstrap/runtime settings such as auth, Docker, transport, or server URL.
- Do not require one MCP server instance per repository.
- Do not solve this by copying repo `meridian.json` into the server runtime folder.
- Do not broaden this into general per-project runtime configuration for unrelated subsystems unless required by the chosen implementation seam.
- Do not index the global user config into the graph unless there is a later, separate need.

## Chosen Direction

Use a two-stage resolver:

1. Resolve shared baseline analysis options once from the server's existing runtime-bound `CodebaseAnalysisOptions`, then overlay global `meridian.json` `analysis.*`.
2. At tool execution time, resolve project-local indexed `meridian.json` `analysis.*` and overlay them for the requested project.
3. Apply explicit per-call overrides last when a tool intentionally broadens or narrows output.

This keeps current operational config behavior intact while making live analysis follow the same global-then-local mental model that the CLI already documents.

## Effective Precedence

Effective precedence at tool execution time:

1. Explicit tool/request overrides
2. Project-local indexed `meridian.json` `analysis.*`
3. Global `%LOCALAPPDATA%\\CodeMeridian\\meridian.json` `analysis.*` or `CODEMERIDIAN_CONFIG_HOME\\meridian.json`
4. Server runtime-bound `analysis` / `CodeMeridian:Analysis`
5. Built-in `CodebaseAnalysisOptions` defaults

Important clarification:

- Items 3 and 4 are both shared defaults, not project-specific config.
- The new requirement is specifically that the `meridian.json` chain behaves as global first, then project-local.
- Existing explicit server runtime configuration should remain supported as a lower shared fallback rather than disappearing.

## Rejected Direction

Do not solve this by copying each repository's `meridian.json` into the server runtime folder or by mutating the runtime folder on every index pass.

Why this is the wrong model:

- a shared server may serve multiple projects at once
- server-local config would become whichever repo wrote last
- the runtime folder is an operational concern, not the per-project analysis source of truth
- global fallback and project-local overrides would become harder to reason about

## Success Criteria

- [ ] A server with only global `meridian.json` analysis settings changes live MCP analysis behavior without requiring a runtime-folder rewrite.
- [ ] A project with local `analysis.ranking.productionOnlyByDefault` in `meridian.json` gets different live tool output than a project using only the global/default baseline.
- [ ] Project-local indexed config overrides global `meridian.json` for the same field.
- [ ] If a project has no indexed `meridian.json` analysis entries, tools still use the global baseline when present, then runtime defaults with no behavior regression.
- [ ] If global config is absent, project-local overrides still work on top of runtime defaults.
- [ ] If either global or project-local config is stale, missing, or partially invalid, tools degrade safely and surface enough diagnostics to explain the fallback.
- [ ] The implementation is generic across analysis tools and does not duplicate merge logic in each tool.
- [ ] Regression coverage proves runtime fallback, global baseline, project override, partial override merge behavior, and stale/missing config behavior.
- [ ] Documentation clearly distinguishes:
  - global user config
  - project-local indexed config
  - server runtime bootstrap config

## Design Constraints

- Respect Clean Architecture boundaries.
- Keep parsing and merge policy in Application-level abstractions; Infrastructure may supply graph-backed and filesystem-backed reads.
- Do not make MCP tools parse raw config graph nodes ad hoc.
- Do not read the global config file from every tool independently.
- Keep effective-option resolution deterministic and testable.
- Avoid introducing a second source of truth for defaults.

## Proposed Architecture

### 1. Introduce a shared baseline analysis provider

Add a shared service that materializes the baseline analysis configuration used by all projects before project-local overrides are applied.

Suggested shape:

- `IGlobalAnalysisOptionsSource`
- output: partial or complete `CodebaseAnalysisOptions` plus source metadata

Responsibilities:

- start from runtime-bound `CodebaseAnalysisOptions`
- load global `meridian.json` from `CodeMeridianConfigFileStore.LoadGlobal()`
- read only `analysis.*`
- overlay global values onto the runtime baseline
- expose source metadata such as `RuntimeOnly`, `RuntimePlusGlobal`, or `GlobalInvalidFallback`

Implementation decision:

- the current `CodeMeridianConfigFileOptions` / `CodeMeridianConfigSnapshot` models do not carry `analysis`
- do not extend those generic CLI snapshot models just to support live MCP analysis resolution
- add a dedicated global-analysis reader that reuses `CodeMeridianConfigFileStore` path resolution without overloading unrelated CLI concerns

### 2. Introduce a project analysis options resolver

Add an Application service that resolves effective analysis settings for one tool call and one project context.

Suggested shape:

- `IProjectAnalysisOptionsResolver`
- input: `projectContext`, optional request-specific overrides
- output: effective `CodebaseAnalysisOptions` plus source metadata describing which layers participated

Responsibilities:

- begin from the shared baseline produced by `IGlobalAnalysisOptionsSource`
- load project-local indexed `analysis.*` overrides when `projectContext` is present
- merge only known `CodebaseAnalysisOptions` fields
- ignore unknown config keys safely
- return diagnostics metadata for logging and optional tool footnotes

### 3. Add a graph-backed local configuration reader

Add an abstraction that reads a project's indexed `meridian.json` analysis entries from the graph in normalized form.

Suggested shape:

- `IProjectConfigurationRepository`
- method like `GetMeridianAnalysisOverridesAsync(projectContext, cancellationToken)`

Infrastructure responsibility:

- query `ConfigurationFile` and `ConfigurationEntry` nodes for the indexed project-local `meridian.json`
- return only `analysis.*` entries
- preserve enough metadata to reason about freshness and file source

Application responsibility:

- translate returned key/value entries into a partial `CodebaseAnalysisOptions` object

### 4. Normalize both global and local config into the same option-path model

Define one canonical mapping from config entries to `CodebaseAnalysisOptions`, regardless of whether the source is:

- runtime config
- global `meridian.json`
- indexed project-local `meridian.json`

Example mapping goals:

- `analysis.ranking.productionOnlyByDefault`
- `analysis.ranking.includeSuppressedNoise`
- `analysis.communityNoise.minimumCommunitySize`
- list values such as `analysis.ranking.suppressedNodeTypes`

Important detail:

- verify how nested JSON keys from indexed `meridian.json` are normalized by the configuration indexer
- use the same path vocabulary when reading global filesystem JSON directly
- if the current canonical key format is not sufficient for safe round-tripping into option paths, add a minimal additive normalization improvement and cover it with tests

### 5. Merge rules must be partial and layer-aware

The resolver should support partial overrides at both the global and project-local layers without requiring a full copy of the entire `analysis` section.

Expected behavior:

- scalar values override the lower layer
- list values replace the lower-layer list when explicitly configured
- unknown fields are ignored
- invalid values for one field should not discard the entire source
- project-local values win over global values for the same option

Recommended initial rule:

- scalar and boolean fields: replace
- arrays/lists: replace the lower-layer list when explicitly configured

Reason:

- it matches normal config expectations better than implicit union behavior
- it keeps project intent obvious
- it makes global baseline plus project-local override easy to reason about

## Tool Integration Plan

### 1. Centralize option resolution before tool logic

Avoid editing each tool independently to read config.

Preferred pattern:

- resolve effective options once per service call
- pass the effective options into shared ranking and filtering helpers or service instance methods

Likely surfaces:

- `CodebaseQueryService`
- helper methods that currently read `analysisOptions`
- any other Application services using `CodebaseAnalysisOptions`

### 2. Preserve request-specific behavior

Tool flags that intentionally broaden output should still win over both global and project-local defaults.

Examples:

- an explicit request for broader heuristic matches
- an explicit request to include suppressed noise
- any future per-call analysis knobs

### 3. Add source metadata to logs and optional diagnostics output

At minimum, log whether effective options came from:

- runtime defaults only
- runtime plus global config
- runtime plus global plus project-local config
- explicit request override plus config layers

Optional follow-up:

- expose a compact footer in exploratory or debug output when a fallback occurred because global or local config was missing or invalid

## Staleness And Fallback Behavior

The resolver must degrade safely when global or local config is unavailable.

Cases to handle:

- global config file is absent
- global config file exists but contains invalid `analysis` values
- project has never been indexed
- project was indexed without `meridian.json`
- config nodes were deleted or are stale
- indexed config contains invalid value types
- project context is omitted

Fallback policy:

- use the next lower layer in the precedence chain
- do not fail the user-facing tool call
- emit structured logs and, where appropriate, compact user-visible notes

## Indexer Follow-Ups

Most of the project-local path already exists, but this plan must explicitly verify:

- [ ] `meridian.json` is always included in default configuration indexing
- [ ] nested `analysis.*` keys from project-local `meridian.json` round-trip into graph entries with enough fidelity for option resolution
- [ ] incremental config re-index updates and deletions keep project override state accurate
- [ ] active architecture-path filtering does not accidentally exclude `meridian.json`

Possible additive indexer work:

- [ ] add clearer per-entry metadata if the current graph does not preserve enough structure to rebuild option paths safely
- [ ] add a focused test proving project-local `meridian.json` nested analysis keys become expected `ConfigurationEntry` records

## Resolved Decisions

- [x] Only `meridian.json` participates in this first implementation. Other config sources stay out of scope.
- [x] The global shared layer comes from `CodeMeridianConfigFileStore.LoadGlobal()`. Runtime config remains the lower fallback already supported today.
- [x] Project-local overrides require exact indexed file path `meridian.json`.
- [x] List-valued options replace lower-layer lists when explicitly configured.
- [x] Invalid values produce structured logs first. User-visible warnings are reserved for debug-oriented outputs or explicit diagnostics surfaces.
- [x] Do not cache effective project options initially. Add caching only if profiling shows repeated graph reads are materially expensive.
- [x] Apply the new resolution model to the live Application analysis flows that currently consume `CodebaseAnalysisOptions`, starting with `CodebaseQueryService` as the central seam.
- [x] Do not surface the effective profile in `doctor` in this first slice. That can be a later diagnostics follow-up if needed.
- [x] Do not add per-tool config scopes in this slice. Shared `analysis.*` remains the only supported contract.

## Detailed Implementation Steps

### Phase 0. Confirm the shared baseline contract

- [ ] Keep runtime `analysis` / `CodeMeridian:Analysis` as the lower shared fallback beneath global `meridian.json`
- [ ] Verify the current global config file location contract in tests and docs:
  - `%LOCALAPPDATA%\\CodeMeridian\\meridian.json`
  - or `CODEMERIDIAN_CONFIG_HOME\\meridian.json`
- [ ] Add a dedicated global-analysis reader that reuses the config-file-store path resolution without extending `CodeMeridianConfigSnapshot`
- [ ] Add a failing test first that proves global `analysis` values are not currently part of the live baseline

### Phase 1. Confirm the project-local graph contract

- [ ] Inspect how the configuration parser emits nested JSON keys for project-local `meridian.json`
- [ ] Verify how those keys are stored and queried through the Neo4j repository
- [ ] Verify whether existing normalized key fields are enough to rebuild `analysis` overrides
- [ ] Add a failing test first if the current key shape is ambiguous

### Phase 2. Add Application abstractions

- [ ] Add `IGlobalAnalysisOptionsSource`
- [ ] Add `IProjectAnalysisOptionsResolver`
- [ ] Add a model representing partial analysis overrides plus source metadata
- [ ] Add an Application-level merge utility from partial overrides into `CodebaseAnalysisOptions`
- [ ] Keep the parsing contract shared between filesystem-backed global reads and graph-backed project-local reads
- [ ] Keep this independent from MCP-specific transport details

### Phase 3. Add shared global config reads

- [ ] Reuse `CodeMeridianConfigFileStore` global path resolution
- [ ] Load only the `analysis` subtree from global `meridian.json`
- [ ] Parse it into the same partial-override model used by project-local resolution
- [ ] Record diagnostics for invalid global values without failing startup or requests

### Phase 4. Add graph-backed project-local config reads

- [ ] Add a repository or query service that loads indexed project-local `meridian.json` `analysis.*` entries for one project
- [ ] Keep the query bounded to a single project and file
- [ ] Return normalized values ready for Application parsing

### Phase 5. Wire resolution into live analysis flows

- [ ] Update `CodebaseQueryService` to resolve effective options per request and project
- [ ] Replace direct use of singleton-captured `analysisOptions` where needed
- [ ] Keep the new resolver seam reusable for other Application services that later consume `CodebaseAnalysisOptions`
- [ ] Preserve behavior for calls without project context
- [ ] Keep the public tool contract stable unless a small diagnostics note is intentionally added

### Phase 6. Add validation and fallback reporting

- [ ] Validate type conversion for booleans, ints, doubles, enums if any, and string lists
- [ ] Record partial-parse warnings without failing the whole request
- [ ] Emit structured logs showing source layers and fallback reasons

### Phase 7. Test the seam thoroughly

- [ ] Unit tests for parsing global `analysis` JSON into partial overrides
- [ ] Unit tests for parsing indexed project-local config entries into partial overrides
- [ ] Unit tests for merge semantics against `CodebaseAnalysisOptions`
- [ ] Application tests proving project A and project B can produce different outputs from the same global baseline
- [ ] Fallback tests for missing global config
- [ ] Fallback tests for missing project-local config
- [ ] Fallback tests for invalid global config values
- [ ] Fallback tests for invalid project-local config values
- [ ] Regression test proving explicit per-call broader behavior still overrides both config layers
- [ ] Indexer tests for nested project-local `meridian.json` analysis entry indexing if current coverage is missing

### Phase 8. Documentation and rollout

- [ ] Update `docs/indexing.md` to explain the split between:
  - shared runtime bootstrap config
  - global user config
  - project-local indexed analysis config
- [ ] Update `docs/installation-global.md` to note that global `analysis.*` acts as the live shared fallback for MCP analysis behavior
- [ ] Update `README.md` and `docs/features.md` if user-facing behavior changes materially
- [ ] Add an operational note telling users they must re-index after changing project-local `meridian.json` analysis settings
- [ ] Link this plan from the earlier noise-reduction plan if that doc remains the historical origin

## Test Plan

Minimum required coverage:

- one parser test for global `meridian.json` `analysis` loading
- one parser test for nested project-local `analysis` keys from indexed `meridian.json`
- one merge test for global baseline plus project-local partial override
- one end-to-end Application test proving global-only config changes `find_hotspots`
- one end-to-end Application test proving project-local config overrides the global baseline
- one end-to-end Application test proving fallback to runtime defaults when both global and project-local config are absent
- one test proving invalid global config only drops the bad field and keeps valid sibling overrides
- one test proving invalid project-local config only drops the bad field and keeps valid sibling overrides
- one test proving explicit broader-output request behavior still wins

Suggested concrete behavioral checks:

- global config sets `productionOnlyByDefault = false` and a project with no local override gets broader sections by default
- project `A` sets `productionOnlyByDefault = true` locally and re-tightens output over the global baseline
- project `B` has no local override and keeps the global broader baseline
- project `A` sets `includeSuppressedNoise = true` locally and sees suppressed noise inline
- global config sets `minimumCommunitySize = 5` and local project `A` overrides it to `3`

## Risks

- If indexed config key normalization is weak, reconstructing nested project-local option paths may become brittle.
- If global config is parsed through an unrelated tooling snapshot model, analysis support may become fragile or incomplete.
- If option resolution is bolted directly into each tool, maintenance cost and drift will grow quickly.
- If user-visible warnings are too noisy, the fix for tool noise could itself add output noise.
- If project overrides are cached incorrectly, tool output may lag behind re-indexed config.

## Deferred Follow-Ups

- Surface the effective global-plus-local analysis profile in `doctor` or another diagnostics endpoint if users need easier runtime introspection.
- Add per-project caching only if profiling shows repeated graph reads are a real cost.
- Consider per-tool config scopes only after the shared `analysis.*` layer is proven insufficient in real use.

## Proposed Implementation Order

- [ ] 1. Confirm global baseline contract and add failing tests
- [ ] 2. Verify project-local graph key fidelity for indexed `meridian.json` analysis entries
- [ ] 3. Add partial-override parsing and merge primitives
- [ ] 4. Add global analysis source and project resolver
- [ ] 5. Integrate the resolver into `CodebaseQueryService`
- [ ] 6. Add fallback diagnostics and structured logs
- [ ] 7. Add Application and indexer regression tests
- [ ] 8. Update docs and link from the earlier noise-reduction plan

## Definition Of Done

- [ ] Live MCP analysis behavior can follow global `meridian.json` defaults without rewriting the runtime folder
- [ ] Live MCP analysis behavior changes per indexed project based on that project's local `meridian.json` `analysis` section
- [ ] Project-local analysis settings override global analysis settings for the same field
- [ ] Shared runtime defaults still work when neither global nor project-local overrides exist
- [ ] No bootstrap, auth, or server-url behavior is unintentionally taken from indexed project config
- [ ] Test coverage exists for global baseline, project override, fallback, invalid-value, and explicit-request-precedence cases
- [ ] Docs explain the three config layers clearly enough that users know:
  - when to edit global config
  - when to edit project-local config
  - when re-indexing is required
