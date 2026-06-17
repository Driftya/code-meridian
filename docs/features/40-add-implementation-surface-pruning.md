# Add Implementation Surface Pruning

- Status: pending
- Priority: P1
- Note: Return fewer, better files by collapsing broad graph matches into edit-ready targets.

**Problem:** Current implementation-surface tools can include broad contracts, infrastructure adapters, generated-adjacent files, docs, and weak graph matches in the same result. That is useful for orientation, but it forces the agent to manually trim the list before editing.

**Goal:** Add a pruning pass that separates edit-ready targets from context-only targets.

**Expected output:**

- A short primary target list, ideally 3 to 5 files for narrow feature work.
- A separate context-only list for interfaces, docs, generated files, and low-confidence matches.
- Clear reasons when a file is excluded from the primary edit set.
- Stronger use of freshness, exact symbol resolution, impact, and test-shield data before ranking.

**Success criteria:**

- Narrow feature sessions have fewer file-only suggestions.
- Exact targets are preferred over broad file matches.
- Manual fallback searches after graph lookup decrease without hiding necessary uncertainty.
