# Add Semantic Graph Hybrid Search

- Status: done
- Priority: P2
- Note: Mix embeddings with graph constraints for better retrieval.

**Feature:** codemeridian hybrid_search "retry policy" --near OrderService --max-hops 3

**Why Neo4j helps:** Combining vector similarity with graph traversal gives richer retrieval than either one alone.

**Expected output:**

- Semantically similar code restricted to the connected area, with tests excluded by default.

## Delivered

- Added the `hybrid_search` MCP tool.
- The query text is embedded first, then Neo4j ranks vector matches and filters them to the selected graph neighborhood.
- `nearNodeId` anchors the neighborhood, `maxHops` controls graph radius, and tests stay excluded by default.
- Added unit coverage for the service orchestration and integration coverage for the Neo4j repository query.
