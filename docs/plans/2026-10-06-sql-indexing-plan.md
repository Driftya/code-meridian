# Offline SQL Indexing With Native Dialect Parsers

Date: 2026-10-06

Status: PostgreSQL MVP implemented (phases 1–4). Later phases remain follow-up work.

## Implementation And Verification

The implementation uses `pgsql-parser` 18.2.8 and `@pgsql/traverse` 18.0.0, with native PostgreSQL grammar version 180004. `tools/SqlIndexer/supports.md` records the exact supported semantics and limits. The extension contract includes a dialect-owned version fingerprint, so adding an analyzer does not require changing PostgreSQL AST handling or the shared incremental cache.

SQL indexing is opt-in through `indexing.sql.enabled`. The CLI supports `--skip-sql`, existing exclusions and file roles, source rules, database scopes, and an explicit search path. A scope is reanalyzed when its source/configuration/parser fingerprint changes, allowing unchanged consumers to bind against changed declarations. SQL publication is a single transactional inventory replacement; failed files retain stale facts, which dependency impact/connection queries exclude.

Verification covers native parser semantics, dialect contract conformance, stable identities and source positions, cache/retry behavior, Core validation, configuration/CLI selection, authenticated API publication, and Neo4j ownership/replay/failure recovery. An isolated Windows installation of the packed CLI verifies worker/WASM restoration, definitions and dependencies, unchanged runs, publication retry, writes, deletion inventory, and syntax failures outside the source checkout. Linux verification is configured in CI; it was not executed locally.

No PL/pgSQL body analysis, second production dialect, detailed column lineage, ORM reconciliation, or database execution was added. Native grammar fidelity does not imply offline catalog/type validation.

## Recommendation

Add a dedicated `tools/SqlIndexer` Node/TypeScript worker. Start with PostgreSQL using `pgsql-parser` and `@pgsql/traverse`. The parser uses PostgreSQL's C parser compiled to WebAssembly through `libpg_query`; this provides native grammar provenance without maintaining platform-specific C bindings in the .NET CLI. Reuse `tools/IndexerShared` for worker options, batches, graph DTOs, and transport. Keep SQL separate from TypeScript source analysis.

Make the extension boundary a **dialect analyzer**, encompassing parsing, AST traversal, and dialect-specific semantics. Each analyzer emits small, parser-independent SQL facts. Share fact validation, graph mapping, publication, and orchestration. Do not make a PostgreSQL AST the interface that future dialects must implement.

This is the best fit for the current repository: Node workers and packaging already exist, while parser fidelity remains tied to PostgreSQL's grammar. A direct .NET P/Invoke wrapper around `libpg_query` would add native build, ABI, memory-management, and OS/architecture packaging responsibilities. Reconsider that option only if eliminating Node becomes a product requirement.

Sources: [pgsql-parser package documentation](https://github.com/constructive-io/pgsql-parser/tree/main/packages/parser), [parser and traversal ecosystem](https://github.com/constructive-io/pgsql-parser), [libpg_query](https://github.com/pganalyze/libpg_query).

## Assumptions And Scope

- "Native parser" means using the database engine's actual grammar; WASM execution is acceptable. This plan states that assumption explicitly.
- PostgreSQL is the first dialect. Other dialects are extension targets, not initial implementations.
- Index checked-in `.sql` files offline. No database connection, query execution, migration execution, or runtime catalog inspection.
- Produce source-backed dependencies, not an authoritative reconstruction of the current database after executing migrations.
- MVP covers table/view declarations, basic table columns and explicit foreign keys, SELECT dependencies, joins, INSERT/UPDATE/DELETE, and supported ALTER operations. It records function declarations but does not claim complete function-body analysis.
- PL/pgSQL bodies, detailed column lineage, embedded SQL in C#/TypeScript, ORM identity reconciliation, templated SQL, and additional dialects follow in later phases.
- Exact parser package versions and supported PostgreSQL grammar versions are established by the compatibility spike, pinned in a lockfile, and advertised in capabilities. Package version numbers alone are not a server-version compatibility guarantee.

## Repository Evidence And Confidence

Graph freshness: exact `IndexExecutionPlanBuilder` node metadata is high confidence; project relationship completeness is medium. Last reported incremental index was 2026-10-06. Freshness metadata does not prove a local file still matches; source was read for the integration targets below.

Minimal context: `build_minimal_context` linked file enumeration, exclusion policy, incremental cache, and CLI tests. Its test suggestions were partly heuristic, so source-level integration tests remain necessary.

Likely edit surface: dedicated SQL worker, CLI selection/settings/runner, configuration schema, shared graph contracts, and infrastructure publication/query support where needed.

Tests to inspect or run: `IndexExecutionPlanBuilderTests`, `IndexCommandHandlerTests`, `IncrementalIndexCacheTests`, new SQL worker fixtures, Core/API contract tests, and Neo4j integration tests.

Risks / unknowns: offline name binding, migration ordering, stale-edge replacement, shared object ownership, parser assets in the packaged CLI, and existing query traversal filters.

The initial feature-map query matched unrelated snippet work. It is not evidence that SQL indexing already exists. Narrow graph lookups and local inspection established these relevant surfaces:

| Existing surface | Integration purpose |
|---|---|
| `tools/Indexer/Cli/IndexExecutionPlanBuilder.cs` | Include enabled SQL files in full and incremental selection |
| `tools/Indexer/Cli/IndexCommandHandler.cs` | Dispatch SQL passes; capabilities, dry run, watch, and exit/cache behavior |
| `tools/Indexer/Cli/NodeIndexerProcessRunner.cs` | Reuse Node process/dependency support through a small SQL runner |
| `tools/Indexer/Cli/ResolvedIndexerSettings.cs`, `IndexCommandSettingsFactory.cs`, `CommandModels.cs`, `Commands/RootCommandFactory.cs` | SQL options and CLI propagation |
| `src/Tooling/Configuration/ToolConfigurationModels.cs`, `ToolConfigurationService.cs`, `CodeMeridianConfigFileStore.cs` | Preserve/read/merge SQL configuration using current local/global rules |
| `src/Tooling/Discovery/ProjectDiscoveryService.cs`, `IndexingExclusionPolicy.cs` | Existing extension detection and exclusions; add a root API only if actually needed |
| `tools/IndexerShared/src/` | Shared worker options, batches, DTOs, and graph client |
| `tools/Indexer/CodeMeridian.Indexer.csproj` | Package worker source/build artifacts, lockfile, and WASM runtime assets |
| `src/Core/CodeGraph/CodeNode.cs`, `CodeEdge.cs` | Existing `DatabaseTable`, `File`, `Contains`, `Reads`, `Writes`, `DependsOn`, and evidence fields |
| `src/Infrastructure/Graph/` | SQL ownership/replacement storage and any necessary Cypher changes |
| `tools/RoslynIndexer/Pipeline/CSharpDatabaseTracingExtractor.cs`, `tools/TsIndexer/src/walker/database-tracing.ts` | Existing database identity conventions for later cross-language reconciliation |

All new names and paths below are proposed. Before implementation, refresh the graph, resolve each existing symbol, inspect its editing context and impact, and identify its focused test shield. Keep Cypher in Infrastructure and parser packages out of Core/Application.

## Architecture And Design Patterns

```text
.NET CLI: discovery -> SQL batch/options -> SQL worker
SQL worker: dialect selection -> dialect analyzer -> SQL facts
SQL facts -> graph mapper -> existing transport -> backend publication
Backend: successful file replacement -> graph queries / MCP facts

PostgreSqlDialectAnalyzer
  -> PostgreSqlParserAdapter (pgsql-parser)
  -> PostgreSQL visitors, scope tracking, and reference resolution
```

Use these patterns where they solve a concrete boundary:

| Pattern | Application | Reason |
|---|---|---|
| [Strategy](https://refactoring.guru/design-patterns/strategy) | `SqlDialectAnalyzer` selected by explicit dialect ID | Replace dialect behavior without branching throughout orchestration |
| [Adapter](https://refactoring.guru/design-patterns/adapter) | `PostgreSqlParserAdapter` | Isolate package APIs, AST versions, initialization, errors, and source locations |
| [Visitor](https://refactoring.guru/design-patterns/visitor) | PostgreSQL AST visitors with explicit statement/query scopes | Separate extraction from parser-owned node structures |
| Composition and a small registry | Map dialect IDs to constructed analyzers | Reject duplicates/unknown dialects and keep selection deterministic |

The registry is not the GoF Factory Method pattern: no creator inheritance is needed. Do not add Abstract Factory, a plugin loader, a universal SQL AST, or an inheritance hierarchy merely to accommodate possible future dialects. Use a new analyzer registration when a second dialect is implemented.

SOLID responsibilities:

- **SRP:** selection chooses the dialect; the parser adapter parses; visitors extract dialect semantics; the mapper builds CodeMeridian graph data; publication owns replacement and transport failures.
- **OCP:** another analyzer implements the same fact contract. New dialects require configuration/composition changes, not changes to PostgreSQL visitors or the indexing algorithm.
- **LSP:** every analyzer returns the same outcome categories and source/evidence guarantees; unsupported syntax is explicit, never fabricated success.
- **ISP:** keep the analyzer contract narrow. Optional body analysis is exposed through capability metadata and internal composition; no dialect must implement fake PL/pgSQL methods.
- **DIP:** orchestration depends on analyzer and publication contracts. Concrete parser packages remain in worker adapters; Core contains only framework-free graph/publication records when needed.

### Minimal Extension Contract

```typescript
interface SqlDialectAnalyzer {
  readonly dialect: string;
  readonly capabilities: SqlDialectCapabilities;
  analyze(input: SqlAnalysisInput): Promise<SqlAnalysisResult>;
}
```

`SqlAnalysisInput` carries selected source files, an unchanged declaration catalog when needed, database scope, resolution settings, and cancellation. `SqlAnalysisResult` carries declarations, reference/join sites, diagnostics, per-file outcomes, parser provenance, and a contract version. Define each actual type in a small dedicated file.

The boundary contains no third-party AST or `unknown` AST payload. PostgreSQL-specific parser and extractor types stay inside `dialects/postgresql/`. A two-pass analyzer first collects declarations, then resolves references against the complete scope catalog. Persist only the facts needed for graph behavior, not a generic AST framework.

## Dialect Configuration

Add proposed `indexing.sql` configuration to `meridian.schema.json`, configuration snapshots/default merge logic, sample configuration, resolved CLI settings, and worker options. Initial fields: `enabled`, `defaultDialect`, `databaseScope`, optional explicitly known `searchPath`, and `sources` with path pattern, dialect, and optional scope override.

Recommended behavior:

- SQL indexing is initially opt-in; PostgreSQL is the default dialect once enabled.
- A matching source rule overrides the project default. Conflicting rules are configuration errors, not arbitrary first-match behavior.
- Dialect is explicit. Do not infer it solely from `.sql` or retry other parsers until one accepts the file.
- SQL selection operates independently of `--skip-typescript`. Provide `--skip-sql`; reuse existing exclusion and file-role policies.
- Database scope is a logical identity configured by the repository, not a credential or connection string.
- Do not assume `public` or an execution-time `search_path` without explicit evidence. Unknown resolution context stays visible.
- Dry run and capabilities report selected files, dialect, parser/grammar version, supported constructs, and unavailable runtime assets.

## SQL Semantics And Graph Model

Reuse existing relationship names instead of introducing synonyms such as `READS_FROM` alongside `Reads`.

| SQL fact | Recommended graph representation |
|---|---|
| Script | Existing `File` node with `language=sql`, dialect, and database scope |
| Object declaration site | File-owned `SqlDeclaration` node and `Contains` edge; proposed Core/shared DTO addition |
| Table identity | Existing `DatabaseTable`; stable identity scoped by project, dialect, database scope, schema/name |
| View, column, function identities | Add `DatabaseView`, `DatabaseColumn`, `DatabaseFunction` only when their phase implements useful behavior |
| Declaration binding | Proposed `Declares`: declaration site -> shared object identity |
| Script/query read or write | Existing `Reads` / `Writes`, with statement evidence and source location |
| View dependency | Existing `DependsOn`, attributed to the originating declaration file |
| ALTER / foreign key / join | Add `Alters`, `References`, `JoinsWith` when implemented; metadata includes kind and source occurrence |
| Unresolved name | File-owned `SqlReference` with reason/status; no asserted dependency to a guessed object |

Keep statements as internal fact records initially. Add public statement nodes only if per-statement navigation needs them. Use stable occurrence IDs for source facts/edges; repeated joins between the same objects must retain independent evidence. First verify the backend edge merge key and shared DTO transport can preserve occurrences; extend them if necessary.

### Identity And Resolution Rules

1. Preserve parser-normalized identifiers and quoting semantics. Do not lowercase quoted identifiers or destructively flatten names containing dots.
2. Scope shared object identity by dialect and logical database scope. Use a stable escaped/structured serialization, not ambiguous string concatenation.
3. CTEs, aliases, nested queries, recursive CTEs, and temporary objects have lexical/script scopes. A CTE named `orders` is not automatically the physical table `orders`.
4. Treat configured schema resolution or known statement-local search-path changes as evidence; do not carry state between unrelated files without an explicit execution manifest.
5. A fully qualified relation absent from declarations may be an external relation of unknown kind. Do not confidently classify every `RangeVar` as a table or conflate views with tables.
6. DML targets and source reads are distinguished. `INSERT ... SELECT`, UPDATE with FROM, DELETE with USING, and writable CTEs may yield both reads and writes.
7. Store syntax extraction evidence separately from binding certainty. An extracted name is not proof of an exact database object. Ambiguous bindings do not participate as exact impact edges.
8. Foreign-key column references require explicit source/target columns or uniquely established keys. Omitted target columns do not justify inventing `id`.
9. Join facts require resolved scoped operands. Preserve self-join aliases, join kind, bounded original predicate, source span, and occurrence identity; a list of visited relations is insufficient to derive joins.
10. Function signatures distinguish overloads. Record supported LANGUAGE SQL bodies separately from PL/pgSQL and other languages.

Store parser/version, resolver version, resolution status/reason, source path/span, and source hash using existing bounded evidence fields and string-valued properties. Convert parser byte offsets to the repository's line/column convention with explicit UTF-8 tests. Do not use deparsed SQL as the original source location.

### Existing Cross-Language Table Nodes

Current C# and TypeScript tracing uses IDs shaped like `${project}::DatabaseTable::${tableName}`. Those IDs lack explicit dialect/database scope. SQL identities must not silently reuse ambiguous legacy IDs.

MVP preserves those existing nodes. A later reconciliation phase links them only when configured scope and qualified names establish a unique target; retain unresolved or ambiguous outcomes otherwise. Do not rewrite both existing indexers as a prerequisite for PostgreSQL file support.

## Incremental Publication And Failure Handling

SQL facts have two ownership levels: declarations/references belong to a source file; database objects are shared identities. A shared object's `FilePath` must not designate a single owning file. Removing one declaration preserves objects still referenced or declared elsewhere.

Implement successful per-file replacement: parse/extract first, validate facts, then atomically replace that file's SQL-owned facts/edges. A deletion removes only its owned data; garbage collection removes shared objects only when no remaining facts reference them. Never remove manual or other-language evidence as part of SQL cleanup.

The existing project-file deletion service and node/edge ingestion are integration points, not proof that these guarantees already exist. Inspect their repository implementations before reuse. If insufficient, add the smallest versioned Core publication contract, Application orchestration, SDK/API adapter, and Infrastructure transaction needed for SQL replacement; avoid refactoring unrelated indexers.

- Syntax failure in one file does not stop analysis of other files. Preserve that file's last successful graph with a visible stale/error status; it is not current successful output.
- Runtime initialization, malformed publication payloads, or transport failures produce a failed run and do not advance successful cache state.
- Unsupported semantic constructs emit explicit diagnostics and a partial-analysis status. Cache that partial result with its capability/version fingerprint; do not retry unchanged unsupported input indefinitely.
- Do not split scripts with `sql.split(';')`. Use parser-supported statement boundaries if verified; otherwise mark the whole malformed file failed. Account for dollar-quoted bodies, comments, quoted literals, psql commands, and COPY payloads.
- A changed declaration can affect unchanged references. Re-resolve affected files from a retained fact catalog; start with reanalysis of the configured SQL scope when declarations change if a dependency-aware path is not yet justified.
- Fingerprint dialect, scope, search-path settings, parser/grammar version, extractor version, and graph contract as well as file contents. A fingerprint change invalidates relevant SQL cache/catalog entries.
- Cancellation terminates workers and prevents incomplete publication/cache success. Bound file size, parsing concurrency, diagnostics, and retained snippets.

## Implementation Sequence And Acceptance Gates

### 1. Native Parser Compatibility Spike

Create isolated PostgreSQL fixtures and exercise `pgsql-parser` with `@pgsql/traverse`. Verify the published package's AST shape, grammar version, diagnostics, initialization, byte offsets, statement boundaries, and offline WASM loading. Upstream examples use differing AST shapes, so wrap the verified version instead of copying examples blindly.

Compare parse-only `libpg-query` consumption only if the deparser dependency materially affects packaging; otherwise keep the documented wrapper. Pin tested published versions and preserve upstream license notices.

Gate: real parsing works on supported Windows/Linux environments with no database connection; a packaged-worker smoke test loads its local WASM assets. Document the tested grammar range and startup/package cost. The spike selects versions; it does not establish semantic completeness.

### 2. Contracts, Ownership, And Configuration

Define the analyzer/fact contract, identity rules, per-file outcomes, provenance, and minimal graph additions. Implement successful file replacement and shared-object ownership before publishing SQL graphs. Add settings/schema/default merge handling and configuration tests.

Gate: graph DTO/API round trips preserve new types, evidence, and edge occurrences; two files can declare/reference one object without destructive ownership; conflicting dialect rules fail clearly.

### 3. PostgreSQL MVP Extraction

Implement parser adapter, scoped visitors, declaration catalog, reference binding, and graph mapping. Support the MVP scope above, including mixed read/write statements and join sites. Keep dynamic SQL, function bodies outside supported coverage, psql commands, and template syntax visible as unsupported/unresolved.

Gate: fixtures produce expected source-backed definitions and dependencies; CTEs/aliases do not create false physical-table links; quoted names and non-ASCII source positions remain correct.

### 4. CLI, Incremental Runs, Packaging, And Query Acceptance

Add SQL runner/command builder and a small SQL pass coordinator. Integrate full/changed/deleted file selection, watch, dry run, capabilities, cache outcomes, and worker assets. Keep additions to the existing large handler small; do not undertake an all-language dispatcher refactor.

Audit existing connection, impact, editing-context, and unresolved-reference queries for new SQL node/edge support. Add SQL query behavior through existing contracts where possible; add a dedicated MCP tool only if existing tools cannot expose a necessary result. All Cypher stays in `src/Infrastructure/Graph/`.

Gate: installed CLI indexes a SQL-only repository; unchanged runs skip work; parser/config upgrades trigger reanalysis; modified/deleted files remove stale owned facts; failed publication leaves retryable cache state. Existing tools answer script reads/writes and transitive view impact, with evidence and ambiguity visible.

### 5. PostgreSQL Enrichment

Add LANGUAGE SQL function-body analysis, then separately evaluate `plpgsql-parser` for static PL/pgSQL statements. Publish capability limits for dynamic EXECUTE, catalog-dependent types, and unsupported languages. Add deeper column lineage only after aliases, star expansion, declared columns, and ambiguous bindings have defined behavior.

Gate: a function declaration is never presented as a fully analyzed body; dynamic SQL stays unresolved; each added construct has parser and semantic acceptance fixtures.

### 6. Second Dialect And Cross-Language Reconciliation

Implement the next requested dialect with its best suitable parser. SQL Server is a useful extension proof: Microsoft's [ScriptDOM](https://github.com/microsoft/SqlScriptDOM) is a vendor-maintained .NET T-SQL parser, not the PostgreSQL AST and not a promise of the server's internal parser. A .NET worker can emit the same versioned fact contract through a process adapter; do not force it into the Node runtime.

Choose MySQL/other parsers only when that dialect is requested and maintenance, licensing, version fidelity, platform support, and incomplete syntax behavior have been evaluated. Engine-derived parsers are preferred where practical; that preference is not an assumption that every engine exposes a usable parser.

Gate: the second dialect passes common fact-contract tests without changes to PostgreSQL extraction or the shared graph mapper for already supported facts. Add scoped C#/TypeScript reconciliation independently, with ambiguous legacy identities covered by tests.

## Focused Verification Matrix

| Area | Required cases |
|---|---|
| Grammar/locations | DDL, DML, view, CTE, quoting, comments, dollar quotes, Unicode, syntax error, unsupported input |
| Binding | Multiple schemas, quoted dots/case, CTE shadowing, alias reuse, self-join, recursive CTE, unknown search path, absent declaration |
| Semantics | INSERT SELECT, UPDATE FROM, DELETE USING, nested subqueries, writable CTE, explicit FK columns, repeated join occurrences |
| Ownership | Two declaration files, declaration deletion with surviving references, changed view dependencies, retained non-SQL/manual edges |
| Failure/retry | Parser startup failure, parse error after successful indexing, ingest failure, cancellation, retry, no successful-cache advance |
| Incremental | Unchanged files, deleted/renamed files, declaration changes affecting unchanged consumers, config/parser fingerprint changes |
| Distribution/query | Packaged local WASM loading, SQL-only repository, dry run/watch/skip SQL, existing impact/connection traversal |
| Extension | Unknown dialect, duplicate registrations, shared contract conformance, differing parser runtime |

Use Vitest in the new worker following existing Node conventions; xUnit/NSubstitute/FluentAssertions for CLI/Core/Application contracts; existing real-Neo4j integration fixtures for transactional replacement and traversal. Run the affected suites plus a packaged CLI smoke test. Benchmarks establish startup time, peak memory, and scope-reanalysis cost before setting practical budgets.

## Completion Criteria

The first usable delivery is phases 1-4: a packaged CLI indexes configured PostgreSQL files offline, preserves source evidence and unresolved cases, supports correct retries and incremental replacement, and answers table/view dependency questions through the existing graph. Update `docs/indexing.md`, `tools/Indexer/README.md`, SQL support/capability documentation, and user-facing feature documentation with the tested limits.

Phases 5-6 are explicit follow-ups. Neither complete PL/pgSQL analysis nor an implemented second dialect is required to call the PostgreSQL MVP complete; the dialect extension contract and its conformance tests are required.
