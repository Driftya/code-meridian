# Add Blast Radius With Confidence

- Status: implemented
- Priority: P2
- Note: Make impact analysis explicit about what is proven versus inferred.

**Feature:** `find_impact` with `includeConfidence`

**Why Neo4j helps:** The graph can separate exact relationships, interface paths, implementation paths, stale nodes, and documentation mentions.

**Expected output:**

- Impact confidence plus proven callers, heuristic callers, and unknown risk.

**Implemented:** Added an optional `includeConfidence` mode to `find_impact`. It now classifies impacted nodes into proven callers, heuristic callers, and unknown-risk nodes by combining path shape, edge confidence, special graph nodes such as `ApiEndpoint`, and freshness signals from indexed metadata. The MCP tool surface exposes this as the `includeConfidence` parameter on `find_impact`.
