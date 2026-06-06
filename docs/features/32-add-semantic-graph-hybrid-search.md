# Add Semantic Graph Hybrid Search

- Status: pending
- Priority: P2
- Note: Mix embeddings with graph constraints for better retrieval.

**Feature:** codemeridian hybrid_search "retry policy" --near OrderService --max-hops 3

**Why Neo4j helps:** Combining vector similarity with graph traversal gives richer retrieval than either one alone.

**Expected output:**

- Semantically similar code restricted to the connected area, with tests excluded by default.
