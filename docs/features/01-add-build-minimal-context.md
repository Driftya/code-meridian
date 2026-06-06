# Add `build_minimal_context`

- Status: done
- Priority: P0
- Note: This is the strongest product fit for CodeMeridian


**Why:** This is the strongest product fit for CodeMeridian. Existing tools already expose the raw ingredients: `get_context_for_editing`, `find_impact`, `find_connection`, `find_coverage_gaps`, `search_documentation`, and `link_external_concept`. A dedicated context-pack builder turns those into the primary value proposition: give Copilot the smallest useful context slice instead of dumping files.

**Outcome:** A coding assistant can ask for a bounded context pack before editing and get only the target, callers, callees, tests, external concepts, and risk notes that matter.

**Suggested tool shape:**

```json
{
  "target": "Method:Payments.OrderService.PlaceOrderAsync(Order,CancellationToken)",
  "goal": "add idempotency key support",
  "maxTokens": 3000,
  "includeTests": true,
  "includeExternalConcepts": true,
  "includeSourceSnippets": false,
  "detailLevel": "Compact"
}
```

**Default output should include:**

- Target node metadata: name, type, file, line, size, summary
- Direct callers and callees
- Implemented interfaces and implementing classes
- Relevant tests or coverage gaps
- Linked external concepts: tables, APIs, topics, services
- Recently changed or high-churn risk signals
- Files likely needed for the edit
- Estimated context token cost

**Effort:** Medium  
**Value:** Very high  
**Risk:** Low, because it can compose existing repository/service methods first.

