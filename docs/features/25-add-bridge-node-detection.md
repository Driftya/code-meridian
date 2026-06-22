# Add Bridge Node Detection

- Status: pending
- Priority: P2
- Note: Find small but structurally important nodes that connect separate parts of the system.

**Feature:** codemeridian find_bridges --project MyApp

**Why Neo4j helps:** Bridge and betweenness-style graph analysis can surface high-connectivity code that line counts miss.

**Expected output:**

- High bridge-risk nodes with the layers they connect and a confidence or risk note.

## Follow-Up Update: Risky Core Analysis

This note should also cover a broader `find_risky_core_nodes` expansion instead of splitting centrality-based risk analysis into a separate duplicate feature.

### Language-Neutral Requirements

- Work over graph relationships emitted by both Roslyn and TsIndexer rather than parser-specific AST details.
- Rank any structurally important code node type that exists in both graphs, such as methods, classes, files, endpoints, repositories, and external concepts.
- Use shared graph relationships such as `Calls`, `References`, `Implements`, `DependsOn`, `Reads`, and `Writes`.

### Additional Signals

- PageRank or equivalent importance scoring
- Betweenness-style bridge scoring
- In-degree and out-degree risk indicators
- Articulation points and bridge edges
- A short explanation of which clusters, layers, or workflows the node connects

### Expanded Output

- Risky core nodes, not only classic bridge nodes
- The structural reason each node is risky
- The count or kind of callers, dependencies, and connected clusters involved
- A suggested next tool when the node looks unsafe to refactor directly
