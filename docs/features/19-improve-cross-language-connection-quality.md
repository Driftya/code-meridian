# Improve Cross-Language Connection Quality

- Status: pending
- Priority: P2
- Note: Current C# and TypeScript indexing share the graph, but true frontend-to-backend tracing needs stronger edges than imports and class relationships.


**Why:** Current C# and TypeScript indexing share the graph, but true frontend-to-backend tracing needs stronger edges than imports and class relationships.

**Plan:** See [docs/plan/2026-06-05-cross-language-route-matching.md](docs/plan/2026-06-05-cross-language-route-matching.md).

**Tasks:**

- Extract HTTP route endpoints from ASP.NET minimal APIs/controllers.
- Extract frontend fetch/axios calls and route strings.
- Link matching API calls to backend endpoint nodes.
- Surface these links in `find_connection` and `build_minimal_context`.

**Effort:** High  
**Value:** High for full-stack repos  
**Risk:** High, because route matching can be heuristic.

