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

## Follow-Up Update: Graph Re-Ranking And Expansion

Use this note for the next hybrid-search slice instead of creating a duplicate feature.

### Goal

Blend full-text, vector retrieval, and graph expansion into one language-neutral context search flow that works across Roslyn and TsIndexer data.

### Follow-Up Scope

- Start with full-text matches and vector matches instead of vector-only retrieval.
- Expand the best candidates through nearby graph relationships before final ranking.
- Re-rank by graph evidence such as direct path strength, node kind, tool/endpoint proximity, and available tests/docs.
- Keep optional filters on node kinds, projects, and confidence level so mixed-language results stay bounded.

### Expected Output

- Top implementation candidates with exact, structural, or related confidence labels
- A short explanation of whether a result came from text match, vector similarity, graph expansion, or a combination
- Language-neutral paths that can mix C#, TypeScript, and external concept nodes when the graph supports them
