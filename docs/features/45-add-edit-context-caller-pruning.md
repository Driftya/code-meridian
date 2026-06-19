# Add Edit-Context Caller Pruning

- Status: pending
- Priority: P2
- Note: Improve `get_context_for_editing` so the caller list is smaller, more actionable, and less noisy for class-level targets.

**Problem:** In the same Driftya refactor session, `get_context_for_editing` returned noisy caller output for `ChainService`, including file-level and irrelevant-looking nodes that were not useful for the actual moderation extraction. The tool was directionally correct that the class had callers, but the result did not help choose the immediate edit path.

**Goal:** Prune and rank edit-context callers so the default output emphasizes direct production callers, exact method-level callers, and nearby tests before broad file-level context.

**Expected output:**

- Separate sections for direct method callers, class/interface callers, test callers, and context-only file callers.
- Default suppression or downranking of duplicate file nodes when exact method or class callers are already present.
- Clear reasons when a caller is heuristic, generated from route metadata, or expanded from a broader file edge.
- Optional detail mode for users who still want the full raw caller list.

**Success criteria:**

- Class-level `get_context_for_editing` results contain fewer file-only or obviously non-actionable caller entries by default.
- Exact callers are easier to spot without manual source inspection.
- Sessions that use `get_context_for_editing` before refactors report higher target usefulness without losing uncertainty visibility.
