# Find Implementation Surface

- Status: done
- Priority: P1
- Note: Graph lookup should help with exact implementation targets, not only broad orientation


**Why:** Graph lookup should help with exact implementation targets, not only broad orientation. When CodeMeridian can only point at a layer or repository surface, the agent still has to fall back to manual file inspection and loses most of the time saved by the graph.

**Goal:** Given a feature goal or concept cluster, return the most likely files, classes, and methods to edit.

**Suggested tool:** `find_implementation_surface`

**Example prompt:**

```text
@copilot What is the best implementation surface for adding stale-knowledge detection?
```

**Desired output:**

- Likely implementation files
- Likely methods to extend
- Why each target was chosen
- Confidence level per target
- Whether the graph data is fresh enough to trust

**Useful signals:**

- Related tool names and service names
- API endpoint names
- Repository methods with matching concepts
- Tests that already cover the same behavior
- Recent churn in the same area
- Exact node IDs when available

**Effort:** Medium  
**Value:** High  
**Risk:** Medium, because the heuristic needs to stay explainable and avoid noisy target suggestions.

**Implemented:** Added `find_implementation_surface`, which ranks likely implementation files from graph matches, concept matches, likely methods/classes, and local freshness checks. Results include confidence, target files, likely symbols, reasons, and freshness status so agents can report whether CodeMeridian provided exact targets or only general areas.

