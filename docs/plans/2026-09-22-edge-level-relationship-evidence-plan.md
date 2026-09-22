# Edge-Level Relationship Evidence Plan

## Objective

Make every persisted relationship explainable at the edge level. A caller should be able to see whether an edge was directly extracted, inferred by a resolver, or left ambiguous, together with the source location and the resolver evidence that produced it.

This complements project-level relationship completeness. Completeness answers “how much of the graph resolved?”; edge evidence answers “why should I trust this particular edge?”

## Proposed contract

Add nullable, first-class fields to the shared edge contract so existing producers and consumers remain compatible:

- `evidenceKind`: `extracted`, `inferred`, `ambiguous`, or `unknown` for legacy edges.
- `evidenceReason`: stable, bounded reason code such as `roslyn_symbol`, `ts_symbol`, `syntax_fallback`, `multiple_candidates`, or `manual_ingest`.
- `resolver`: producer and strategy identifier, for example `roslyn.semantic`, `roslyn.syntax`, `ts-morph.symbol`, `ts-morph.syntax`, or `manual`.
- `sourceFilePath`, `sourceLine`, `sourceColumn`, `sourceEndLine`, and `sourceEndColumn` where the producer has a precise span.
- `evidenceDetails`: bounded structured details for resolver-specific facts, such as receiver type, target declaration, candidate count, or fallback reason.

Keep `confidence` for numeric scoring and keep `Properties` for existing specialized metadata. Do not encode the new contract only as arbitrary string properties; that would make querying, validation, and MCP output inconsistent.

Use `unknown` when reading old edges or ingesting edges from clients that do not provide evidence. Do not silently label legacy data as extracted.

## Work items

### 1. Establish the canonical schema

- [x] Add a shared evidence-kind type and validation rules in `src/Core/CodeGraph/CodeEdge.cs`.
- [x] Add the nullable evidence fields to `CodeEdge`, `CodeEdgeIngestRequest`, and `tools/IndexerShared/src/types.ts` (`CodeEdgeDto`).
- [x] Decide and document field bounds: maximum reason/resolver length, maximum detail keys, and allowed location ranges.
- [x] Add a graph contract/schema version for the new edge properties; preserve reads of the previous schema.
- [x] Document the contract in `docs/indexing.md` and the SDK/MCP ingestion documentation.

### 2. Persist and read evidence safely

- [x] Extend `Neo4jCodeGraphRepository` edge upsert parameters and reserved-property handling.
- [x] Map evidence fields back from Neo4j in `MapToCodeEdge` and through `GraphRelationship`/read-repository projections.
- [x] Ensure merge identity remains unchanged unless a separate edge identity is required; evidence updates must update an existing relationship rather than create duplicates.
- [x] Define migration behavior for existing edges: retain `unknown` until re-indexed, and expose counts of unknown evidence in diagnostics.
- [x] Add bounds and sanitization at API/SDK ingestion boundaries so arbitrary nested data cannot enter relationship properties.

### 3. Emit evidence from the Roslyn indexer

- [x] Add evidence fields to `IngestEdgeRequest` and preserve them through `CSharpAstWalker`, reference resolution, call resolution, route extraction, configuration extraction, and database tracing.
- [x] Mark direct declaration and compiler-symbol matches as `extracted` with `roslyn.semantic` and a source span.
- [x] Mark syntax-safe or name-based fallback matches as `inferred` with `roslyn.syntax` and a stable fallback reason.
- [x] Mark multiple viable targets, indeterminate callbacks, and unresolved-but-plausible candidates as `ambiguous`; retain the existing resolution disposition separately.
- [x] Populate resolver details from existing semantic receiver/target hints instead of duplicating them in unrelated property keys.
- [x] Update `RelationshipResolutionCollector` so samples include evidence kind, resolver, and source span.

### 4. Emit evidence from TypeScript and frontend indexers

- [x] Add the same fields to TypeScript, JavaScript, JSX, HTML, and CSS edge construction through `IndexerShared`.
- [x] Mark ts-morph symbol and module-resolution matches as `extracted` with `ts-morph.symbol`.
- [x] Mark syntax/module-path or bounded heuristic matches as `inferred` with an explicit reason.
- [x] Mark dynamic imports, computed properties, unresolved callbacks, and multiple candidates as `ambiguous` where the relationship is retained.
- [x] Preserve the distinction between `external_or_unindexed`, `unresolved_local`, and `indeterminate`; evidence kind describes proof quality, while disposition describes resolution outcome.

### 5. Expose evidence in query and MCP results

- [x] Include evidence fields in relationship arrays, path steps, impact findings, changed-subgraph output, endpoint traces, and `get_context_for_editing` where an edge is returned.
- [x] Add an opt-in detail level if payload size would otherwise grow substantially; compact output should include kind, reason, resolver, and location, while full output may include details.
- [x] Add a focused relationship-evidence query/report that groups edges by evidence kind, resolver, reason, and file role.
- [x] Update Markdown output to show a short evidence label and source location without dumping the full details map.
- [x] Ensure typed MCP schemas and SDK models remain backward compatible for clients that ignore new fields.

### 6. Connect evidence to relationship health

- [x] Add counts for `extracted`, `inferred`, `ambiguous`, and `unknown` to relationship-health metadata.
- [x] Include representative samples with source locations and resolver reasons in `check_graph_freshness` and `report relationship-health`.
- [x] Keep the existing completeness thresholds unchanged initially; use the new evidence distribution for diagnosis before changing trust policy.
- [x] Add remediation guidance that points to the relevant resolver or source span instead of only saying “relationship completeness is low.”

### 7. Tests and fixtures

- [x] Add core contract serialization and validation tests.
- [x] Add Neo4j repository round-trip tests for all evidence fields, legacy edges, null fields, and updates without duplicate relationships.
- [x] Add Roslyn fixtures for semantic resolution, syntax fallback, multiple candidates, callbacks, external targets, and source spans.
- [x] Add TypeScript fixtures for symbol resolution, module resolution, dynamic/computed cases, and incremental indexing.
- [x] Add MCP/API tests asserting bounded typed evidence output and backward-compatible omission/null behavior.
- [x] Add relationship-health tests proving evidence counts and samples are deterministic.
- [x] Run the existing Application, Roslyn, TypeScript, MCP, and SDK suites plus a full self-index and verify evidence distributions.

## Delivery order

1. Shared contract and Neo4j round trip.
2. Roslyn and TypeScript producers.
3. MCP/query projections and relationship-health reporting.
4. Frontend/configuration/manual producers.
5. Migration documentation, benchmark fixtures, and release notes.

## Acceptance criteria

- A newly indexed C# or TypeScript call edge includes a valid evidence kind, resolver, reason, and source location whenever the parser can provide one.
- Semantic matches are distinguishable from fallback and ambiguous matches without inspecting opaque property strings.
- Existing edges and clients continue to work; old edges read as `unknown` and are not misrepresented as proven.
- Relationship-health output can identify the files and resolver reasons responsible for low trust.
- Re-indexing the CodeMeridian repository produces deterministic evidence counts and no increase in duplicate relationships.

## Risks and decisions to preserve

- Do not derive `extracted` solely from a high numeric confidence score; provenance and confidence are different dimensions.
- Do not collapse unresolved/external/indeterminate accounting into evidence kind; both dimensions are needed to explain completeness.
- Avoid storing unbounded source text or full compiler objects on relationships. Store locations and bounded summaries, with node snippets remaining the source of larger context.
- Treat evidence fields as a versioned public contract because they will be consumed by MCP clients, SDK callers, reports, and future indexers.
