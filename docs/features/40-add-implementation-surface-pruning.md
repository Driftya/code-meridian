# Add Implementation Surface Pruning

- Status: implemented
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

**Implemented:** `find_implementation_surface` now runs a pruning pass after ranking. It separates a short primary edit set from context-only targets, demotes documentation, tests, generated/build artifacts, configuration-adjacent files, stale matches, contract-only files, and broad file-only matches, and explains each exclusion inline. When every candidate is contextual, the best available target is still promoted so the tool never returns an empty primary edit set.
