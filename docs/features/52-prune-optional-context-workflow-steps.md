# Prune Optional Context Workflow Steps By Default

- Status: implemented
- Priority: P2
- Note: Workflow planning is valuable, but narrow tasks should not default to broad optional-step itineraries.

**Problem:** `plan_context_workflow` and `execute_context_workflow` can still feel noisy because recipe breadth is often useful for orientation but not for a small, exact task.

**Goal:** Keep workflow planning deterministic while pruning optional breadth for narrow tasks unless the caller explicitly asks for it.

**Expected output:**

- Fewer optional steps in narrow before-edit and fix workflows.
- Clearer distinction between required steps and awareness-only additions.
- Better defaults without removing access to the broader recipe when requested.

**Implemented:** `plan_context_workflow` and `execute_context_workflow` now treat `includeOptionalSteps` as workflow-aware by default. Narrow workflows such as `before_edit`, `diagnostic_review`, `configuration_review`, and `dependency_replacement` prune optional awareness-only steps unless the caller explicitly sets `includeOptionalSteps=true`, while `includeOptionalSteps=false` still forces required-only plans across every workflow. The planner also emits a warning when default pruning was applied so callers know how to request the broader recipe.
