# Detect Graph Drift Before Implementation

- Status: done
- Priority: P1
- Note: If CodeMeridian is indexed out of date, the agent can still get the right architectural direction but miss exact file targets


**Why:** If CodeMeridian is indexed out of date, the agent can still get the right architectural direction but miss exact file targets. A built-in drift check would tell the agent whether it should trust the graph or re-index first.

**Suggested tool:** `find_graph_drift`

**Possible checks:**

- Nodes whose files no longer exist
- Nodes whose line ranges no longer fit the source file
- Nodes updated before the last major reindex
- Projects with many renamed or deleted files
- Query surfaces that return broad layers but no exact method IDs

**Desired output example:**

```text
Graph drift: moderate
Reason: 14 nodes point to missing files, 6 node line ranges no longer match source, and the last reindex predates the latest rename.
Recommendation: run `codemeridian index . --project CodeMeridian --clear`
```

**Effort:** Medium  
**Value:** High  
**Risk:** Medium, because drift detection needs accurate file and timestamp metadata.

**Implemented:** Added `find_graph_drift`, which checks indexed nodes for missing files, invalid line ranges, and missing timestamps, then reports drift severity and a re-index recommendation when exact implementation targeting should not be trusted.

