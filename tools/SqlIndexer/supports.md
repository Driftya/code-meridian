# SQL Indexer Support

SQL indexing is opt-in via `indexing.sql.enabled` in `meridian.json`. The SQL worker is independent of TypeScript indexing and uses PostgreSQL's C grammar compiled to WASM, without a database connection.

## Verified Parser

- `pgsql-parser` 18.2.8; `libpg-query` 18.1.5; reported grammar version 180004.
- `@pgsql/traverse` 18.0.0, with explicit handling of untagged DML targets.
- Package versions are pinned in the worker lockfile. Runtime dependencies restore using `npm ci`; first-time restoration requires registry access. Parsing after installation uses local WASM assets.
- The grammar version describes the bundled parser, not a guarantee of compatibility with every PostgreSQL server version.

## Supported Facts

Table/column/view and function declarations; SELECT reads; INSERT/UPDATE/DELETE writes and source reads; scoped CTEs and subqueries; direct joins including aliases/self-joins/repeated occurrences; explicit table and column foreign keys; ALTER targets and added foreign keys; source locations and parser provenance.

Known repository relations use `DatabaseTable` or `DatabaseView`. Qualified names without declarations use `DatabaseRelation`, because the offline parser cannot identify their catalog kind. Unresolved names become file-owned `SqlReference` nodes instead of guessed dependencies. Script declarations and references are file-owned; database identities are shared across files within a dialect and logical database scope.

## Resolution And Limits

Unqualified names require exactly one configured `searchPath` schema for dependency binding. Multiple schemas remain unresolved because an offline index cannot prove absence of a shadowing object. Explicit qualified references remain usable without a search path. Source patterns use relative paths and `*`, `**`, and `?`; overlapping source rules fail clearly.

Function bodies are not analyzed. Temporary objects, unsupported statement semantics, inheritance/partition dependencies, omitted foreign-key target columns, and derived/CTE/nested join operands report partial coverage. The join predicate is normalized from the AST; source positions refer to the original script. Detailed column lineage, migration execution ordering, runtime search-path simulation, templates, psql commands, COPY data payloads, and dynamic SQL are outside this release. Syntax errors retain the last successful graph with stale status and cause a retryable failed run.

Each file is limited to 4 MiB, with 64 MiB of source and 10,000 files per scope. Inputs are parsed sequentially. Every changed SQL inventory/configuration/parser fingerprint reanalyzes the configured SQL scope. Successful publication atomically replaces owned facts, removes deleted files, and preserves shared objects still referenced elsewhere. Stale SQL edges are excluded from normal impact/connection traversal. One indexing process per project context is expected; publication serializes concurrent writes but does not establish source-revision ordering.

## Configuration

```json
{
  "indexing": {
    "sql": {
      "enabled": true,
      "defaultDialect": "postgresql",
      "databaseScope": "application",
      "searchPath": ["public"],
      "sources": [
        { "pattern": "analytics/**/*.sql", "dialect": "postgresql", "databaseScope": "analytics" }
      ]
    }
  }
}
```

Run `codemeridian index .`; use `--skip-sql` to skip SQL, `--dry-run` to inspect selection, or `--list-capabilities` to inspect the parser and asset requirements. `--no-incremental` forces SQL reanalysis. Additional dialects implement the fact-based analyzer contract and register independently; no second dialect is bundled yet. Existing legacy C#/TypeScript table IDs are preserved and are not automatically reconciled with SQL identities.

## Upstream Licenses

The installed `pgsql-parser`, traversal/types, and deparser packages provide their upstream MIT license notices. `libpg_query` includes PostgreSQL and BSD-licensed components; retain the notices distributed in `libpg-query`. See [pgsql-parser](https://github.com/constructive-io/pgsql-parser) and [libpg_query](https://github.com/pganalyze/libpg_query).
