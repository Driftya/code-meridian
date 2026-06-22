# Add Endpoint To Database Tracing

- Status: implemented
- Priority: P2
- Note: Trace a web request through the full vertical slice.

**Feature:** codemeridian trace_endpoint POST /api/orders

**Why Neo4j helps:** Neo4j can connect HTTP endpoints, use cases, repository calls, database tables, topics, docs, and tests.

**Expected output:**

- An endpoint path from API route to use case, repository, table, and event.

**Implemented:**

- Added repo-owned `.meridian/database-tracing.json` plus packaged `database-tracing.sample.json`.
- Bound `DatabaseTracing` options in the application runtime and the Roslyn and TypeScript indexer startup paths.
- Added language-neutral database recognizer pipelines that emit `DatabaseOperation` external concepts and `DatabaseTable` nodes from both C# and TypeScript.
- Shipped starter presets for EF Core, Dapper, raw SQL, Prisma, Knex, and Neo4j Cypher recognition.
- Added graph-backed `trace_endpoint` support through MCP, REST, SDK, and CLI surfaces.
- Expanded structural path traversal so `Reads`, `Writes`, `PublishesTo`, and `SubscribesTo` participate in route tracing and generic connection queries.
