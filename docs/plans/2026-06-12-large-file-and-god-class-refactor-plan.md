# Large File And God Class Refactor Plan

Date: 2026-06-12

## Goal

Reduce the largest SRP violations in CodeMeridian by splitting oversized classes and files by concern, while keeping behavior stable and preserving the current Clean Architecture boundaries.

This plan is driven by CodeMeridian hotspot analysis rather than folder-based intuition.

## Why This Work Matters

Large files in this repository are not just a style issue. They are already mixing orchestration, parsing, normalization, process execution, file IO, and graph-specific behavior in ways that make changes harder to test and reason about.

The practical outcomes we want are:

- Smaller files with one dominant responsibility.
- Fewer regression-prone changes when adding features.
- Better unit-test focus around narrow components.
- Easier future work on route extraction, indexing, and config generation.
- Stronger alignment with the existing Clean Architecture and SOLID rules in `AGENTS.md`.

## Inputs From CodeMeridian

CodeMeridian `find_large_nodes` on project `CodeMeridian` reported these top oversized classes:

- `IndexCommandHandler` — `tools/Indexer/Cli/IndexCommandHandler.cs` — 600 lines
- `CSharpRouteExtractor` — `tools/RoslynIndexer/Pipeline/CSharpRouteExtractor.cs` — 494 lines
- `ServeWriter` — `tools/Indexer/Cli/ServeWriter.cs` — 474 lines
- `Neo4jVectorRepository` — `src/Infrastructure/Knowledge/Neo4jVectorRepository.cs` — 424 lines
- `CSharpAstWalker` — `tools/RoslynIndexer/Pipeline/CSharpIndexer.cs` — 351 lines
- `DocumentIndexerPipeline` — `tools/DocumentIndexer/Pipeline/DocumentIndexerPipeline.cs` — 306 lines
- `CSharpIndexer` — `tools/RoslynIndexer/Pipeline/CSharpIndexer.cs` — 303 lines

CodeMeridian `find_god_classes` errored, so the hotspot workflow itself also needs repair before we depend on it for refactor planning.

## First Fix

- [ ] Fix the `find_god_classes` MCP tool failure.
- [ ] Add or update a regression test that proves `find_god_classes` returns ranked results instead of failing.
- [ ] Re-run `find_large_nodes` and `find_god_classes` after the fix and refresh this plan if the ranking materially changes.

## Refactor Order

Recommended order based on size, concern-mixing, and expected blast radius:

1. `IndexCommandHandler`
2. `CSharpRouteExtractor`
3. `ServeWriter`
4. `DocumentIndexerPipeline`
5. `CSharpIndexer` and embedded `CSharpAstWalker`
6. `Neo4jVectorRepository`
7. `tools/TsIndexer/src/walker.ts`

## Target 1: IndexCommandHandler

Current concern mix:

- command orchestration
- project discovery
- incremental planning
- C# run coordination
- TypeScript run coordination
- document run coordination
- diagnostics coordination
- watch mode
- file filtering
- process execution
- temp file writing
- project-file deletion API calls

Target extraction shape:

- `IndexExecutionPlanBuilder`
- `CSharpIndexRunCoordinator`
- `TypeScriptIndexRunCoordinator`
- `DocumentIndexRunCoordinator`
- `IndexWatchLoop`
- `ProjectFileDeletionService`
- `TypeScriptIndexerProcessRunner`

Checklist:

- [x] Extract TypeScript command construction into a dedicated helper with regression tests.
- [ ] Extract file filtering and changed-file selection logic out of `IndexCommandHandler`.
- [ ] Extract process launching and TypeScript dependency/bootstrap logic into a dedicated runner/service.
- [ ] Extract watch-mode debounce and scheduling into a dedicated loop/service.
- [ ] Extract document indexing coordination into a dedicated coordinator.
- [ ] Extract delete-project-file behavior into a dedicated service.
- [ ] Keep `IndexCommandHandler` as a thin orchestration entry point.
- [ ] Add or update unit tests around each extracted service.
- [ ] Re-run full `dotnet test` and root `npm test`.

## Target 2: CSharpRouteExtractor

Current concern mix:

- controller route extraction
- minimal API route extraction
- route normalization
- const/local string resolution
- route source resolution
- `MapGroup` and `MapMethods` composition
- handler matching and confidence assignment

Target extraction shape:

- `AspNetControllerRouteExtractor`
- `MinimalApiRouteExtractor`
- `RouteTemplateNormalizer`
- `RouteConstantResolver`
- `RouteSourceResolver`

Checklist:

- [ ] Extract route normalization into a dedicated component with focused tests.
- [ ] Extract controller route parsing from minimal API parsing.
- [ ] Extract string/const route resolution into its own helper/service.
- [ ] Extract source-method resolution into its own helper/service.
- [ ] Keep a thin top-level route extraction façade that coordinates the subcomponents.
- [ ] Add regression tests for existing bugs already found in normalization and absolute URL handling.
- [ ] Re-run Roslyn indexer tests and full repo tests.

## Target 3: ServeWriter

Current concern mix:

- `.env` generation and merging
- compose writing
- VS Code MCP config
- Codex TOML config
- Continue MCP config
- TOML merge logic
- backup and overwrite semantics

Target extraction shape:

- `EnvFileWriter`
- `ComposeFileWriter`
- `McpJsonWriter`
- `CodexTomlWriter`
- `ContinueConfigWriter`
- `ConfigFileMergeUtility`

Checklist:

- [ ] Extract content-generation from file-write side effects.
- [ ] Extract TOML/JSON merge behavior into reusable helpers.
- [ ] Extract backup/overwrite policy handling into a shared utility.
- [ ] Keep `ServeWriter` only as a façade if still needed.
- [ ] Add or update tests per writer and per merge utility.

## Target 4: DocumentIndexerPipeline

Current concern mix:

- ingestion orchestration
- markdown document reference extraction
- code file reference extraction
- route reference extraction
- MCP tool reference extraction
- chunk relationship building
- path normalization

Target extraction shape:

- `DocumentReferenceExtractor`
- `DocumentCodeReferenceExtractor`
- `DocumentRouteReferenceExtractor`
- `DocumentMcpToolReferenceExtractor`
- `DocumentChunkReferenceBuilder`

Checklist:

- [ ] Move reference extraction logic out of the pipeline class.
- [ ] Move chunk adjacency/document-id building out of the pipeline class.
- [ ] Keep `DocumentIndexerPipeline` focused on file IO and ingestion orchestration.
- [ ] Add focused tests for each extractor component.
- [ ] Preserve the current incremental document indexing behavior added through the CLI path.

## Target 5: CSharpIndexer And CSharpAstWalker

Current concern mix:

- AST walking and node creation
- invocation edge generation
- call edge resolution
- reference edge resolution
- batching and transport concerns

Target extraction shape:

- `CSharpAstWalker`
- `CSharpCallEdgeResolver`
- `CSharpReferenceEdgeResolver`
- `CSharpBatchIngestionWriter`

Checklist:

- [ ] Split AST walking from post-processing resolution.
- [ ] Split call-edge and reference-edge resolution into dedicated components.
- [ ] Split ingestion batching/transport concerns from graph construction.
- [ ] Add targeted resolver tests for ambiguous call and reference cases.

## Target 6: Neo4jVectorRepository

Current concern mix:

- initialization
- upsert/search operations
- graph/document mapping
- normalization helpers

Checklist:

- [ ] Split mapping and normalization helpers from repository write/read operations.
- [ ] Evaluate whether initialization belongs in a separate setup service.
- [ ] Add targeted tests for mapping and normalization helpers if extracted.

## Target 7: TypeScript walker.ts

Current concern mix:

- node collection
- edge collection
- call resolution
- symbol resolution
- route extraction
- route normalization
- test-case discovery

Target extraction shape:

- `walker/nodes`
- `walker/edges`
- `walker/routes`
- `walker/symbol-resolution`
- `walker/test-discovery`

Checklist:

- [ ] Split route extraction from general graph walking.
- [ ] Split node collection from edge collection.
- [ ] Split symbol/call resolution from AST traversal.
- [ ] Keep the public `walkTypeScript` entry point stable.
- [ ] Preserve the expanded TS regression suite while moving code.

## Namespace And Folder Guidance

Prefer splitting by concern before inventing deeper namespaces.

Likely structure:

- `tools/Indexer/Cli/Coordination/`
- `tools/Indexer/Cli/Watch/`
- `tools/Indexer/Cli/ConfigWriters/`
- `tools/RoslynIndexer/Pipeline/Routes/`
- `tools/RoslynIndexer/Pipeline/Resolution/`
- `tools/DocumentIndexer/Pipeline/References/`
- `tools/TsIndexer/src/walker/`

Rules:

- Keep orchestration entry points shallow.
- Move pure helpers out of service classes when they can be tested independently.
- Do not move infrastructure concerns into `Core` or `Application`.
- Preserve existing public command behavior unless a deliberate UX change is documented.

## Validation Strategy

For each refactor target:

- [ ] Add characterization tests before moving behavior when risk is high.
- [ ] Move one concern at a time, not multiple unrelated extractions in one step.
- [ ] Run the smallest relevant suite after each extraction.
- [ ] Run full `dotnet test` and root `npm test` after each target is complete.
- [ ] Re-index and re-run CodeMeridian hotspot tools after major splits.

## Suggested Execution Slices

Slice 1:

- [ ] Fix `find_god_classes`
- [ ] Refactor `IndexCommandHandler`

Slice 2:

- [ ] Refactor `CSharpRouteExtractor`
- [ ] Refactor `ServeWriter`

Slice 3:

- [ ] Refactor `DocumentIndexerPipeline`
- [ ] Refactor `CSharpIndexer` support pieces

Slice 4:

- [ ] Refactor `Neo4jVectorRepository`
- [ ] Refactor `walker.ts`

## Done Criteria

The plan is complete when:

- [ ] `find_god_classes` works again and is covered by tests.
- [ ] Each target file is materially smaller and has a narrower responsibility.
- [ ] Public behavior remains stable unless intentionally documented otherwise.
- [ ] Regression tests cover the extracted responsibilities directly.
- [ ] Full repo tests pass after each target refactor.
- [ ] CodeMeridian hotspot scans show reduced concentration in the original god classes.
