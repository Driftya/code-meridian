# Prune Optional Context Workflow Steps By Default

- Status: pending
- Priority: P2
- Note: Workflow planning is valuable, but narrow tasks should not default to broad optional-step itineraries.

**Problem:** `plan_context_workflow` and `execute_context_workflow` can still feel noisy because recipe breadth is often useful for orientation but not for a small, exact task.

**Goal:** Keep workflow planning deterministic while pruning optional breadth for narrow tasks unless the caller explicitly asks for it.

**Expected output:**

- Fewer optional steps in narrow before-edit and fix workflows.
- Clearer distinction between required steps and awareness-only additions.
- Better defaults without removing access to the broader recipe when requested.
