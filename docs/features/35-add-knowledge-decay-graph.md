# Add Knowledge Decay Graph

- Status: pending
- Priority: P2
- Note: Turn stale-knowledge detection into a graph-native view.

**Feature:** codemeridian knowledge_decay

**Why Neo4j helps:** Persistent docs and concepts can be evaluated against deleted nodes, orphaned links, and old references.

**Expected output:**

- Stale documentation clusters, orphaned concepts, and deleted-node references.

**Implemented:** Exposed the existing graph-backed stale-knowledge analysis under the requested `knowledge_decay` command as an alias of `find_stale_knowledge`. This keeps one implementation surface while supporting the "knowledge decay" workflow language. The output already includes unresolved document mentions, orphaned external concepts, stale notes, and orphaned code references.
