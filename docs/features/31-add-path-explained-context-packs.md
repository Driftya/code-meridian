# Add Path-Explained Context Packs

- Status: pending
- Priority: P2
- Note: Explain why each file is included in a context pack.

**Feature:** codemeridian context-pack --explain

**Why Neo4j helps:** Neo4j paths let the assistant show relevance instead of only asserting it.

**Expected output:**

- Included files with the path from the target to the file and any nearby diagnostics or tests.

**Implemented:** Added an optional `explainPaths` mode to `build_minimal_context`. When enabled, the context pack emits file-inclusion explanations with a representative graph path from the target plus nearby indexed diagnostics and target-adjacent tests instead of only listing file paths.
