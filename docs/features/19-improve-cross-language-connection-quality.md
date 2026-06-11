# Improve Cross-Language Connection Quality

- Status: done
- Priority: P2
- Note: Current C# and TypeScript indexing share the graph, but true frontend-to-backend tracing needs stronger edges than imports and class relationships.


**Why:** Current C# and TypeScript indexing share the graph, but true frontend-to-backend tracing needs stronger edges than imports and class relationships.

**Plan:** See [docs/plans/2026-06-05-cross-language-route-matching.md](../plans/2026-06-05-cross-language-route-matching.md).

**Implemented:**

- Extracted `ApiEndpoint` nodes from ASP.NET controller attributes and minimal API route maps.
- Extracted frontend route calls from `fetch`, `axios`, and HTTP-like wrapper methods in the TypeScript indexer.
- Linked frontend callers and backend handlers/actions to shared `ApiEndpoint` nodes so `find_connection` can traverse full-stack route paths.
- Added document mention inference for explicit route strings such as `POST /api/orders`.
- Added document mention inference for explicit MCP tool attribute snippets such as `[McpServerTool(Name = "find_connection")]`.

**Notes:**

- The first slice is intentionally conservative. It favors static route literals, simple templates, local constants, and local route maps.
- Route matching still avoids deep runtime inference, generated clients, and opaque wrapper chains.
- Re-index the project after upgrading so the new `ApiEndpoint` nodes and cross-language edges appear in the graph.

