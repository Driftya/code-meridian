# Add Graph Freshness And Confidence Signals

- Status: done
- Priority: P1
- Note: A graph lookup is only as good as the data behind it


**Why:** A graph lookup is only as good as the data behind it. The assistant needs to know whether a result is exact, heuristic, stale, or partially verified before it decides whether to trust the graph or fall back to source files.

**Add to result payloads:**

- `indexedAt`
- `updatedAt`
- `fileExists`
- `lineRangeStillValid`
- `nodeIdConfidence`
- `freshnessReason`

**Desired behavior:**

- Exact node hits should say why they are exact.
- Heuristic matches should say what was inferred.
- Stale or missing source should be explicit.
- Tools should return a short trust summary, not just raw facts.

**Effort:** Medium  
**Value:** High  
**Risk:** Medium, because it touches many result formatters and tool descriptions.

**Implemented first slice:** Added `check_graph_freshness`, which reports `updatedAt`, file existence, line-range validity, confidence, and reason for matching graph nodes. `find_implementation_surface` also includes per-target freshness and confidence. Broader annotation across every existing formatter can be expanded later if needed.

