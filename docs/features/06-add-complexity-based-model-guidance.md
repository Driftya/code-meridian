# Add Complexity-Based Model Guidance

- Status: done
- Priority: P1
- Note: Once token estimates and graph size are known, CodeMeridian can recommend whether a small, fast model is enough or whether a larger context/model is justified.


**Why:** Once token estimates and graph size are known, CodeMeridian can recommend whether a small, fast model is enough or whether a larger context/model is justified.

**Signals:**

- Estimated token count
- Number of affected nodes from `find_impact`
- Number of downstream dependencies
- Cross-project edges
- High-churn or hotspot status
- Missing tests
- External concepts involved

**Output example:**

```text
Model guidance: use a larger model.
Reason: estimated 18,000 tokens, 42 affected nodes, 3 cross-project dependencies, missing test coverage.
```

**Effort:** Low to medium  
**Value:** Medium to high  
**Risk:** Low.

**Implemented:** Context packs now include a `Complexity`, `Model guidance`, and `Expansion risk` line. Guidance is based on estimated tokens, affected nodes, downstream dependencies, cross-project graph edges, nearby coverage gaps, related-test availability, target size, and indexed churn.

