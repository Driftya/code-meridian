# Add Bridge Node Detection

- Status: pending
- Priority: P2
- Note: Find small but structurally important nodes that connect separate parts of the system.

**Feature:** codemeridian find_bridges --project MyApp

**Why Neo4j helps:** Bridge and betweenness-style graph analysis can surface high-connectivity code that line counts miss.

**Expected output:**

- High bridge-risk nodes with the layers they connect and a confidence or risk note.
