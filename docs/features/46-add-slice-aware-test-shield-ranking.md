# Add Slice-Aware Test Shield Ranking

- Status: pending
- Priority: P1
- Note: Turn `find_test_shield` into a smaller verification planner for the selected seam, not just a broad shield inventory.

**Problem:** `find_test_shield` was still useful in the Driftya moderation refactor because it showed the deletion path had weak direct coverage, but it did not cleanly identify the smallest high-value tests to add. The session result was decent but still broad: 2 of 3 suggested tests were changed or run, which is better than older sessions but still leaves room to tighten the primary recommendation set.

**Goal:** Make test-shield output seam-aware so refactor and extraction workflows get a short primary verification list plus a clearly secondary heuristic list.

**Expected output:**

- A primary section for tests that directly protect the selected method, exact callers, or extracted responsibility slice.
- A secondary section for indirect or heuristic shield tests that remain useful for awareness but are not mixed into the main recommendation.
- Ranking that prefers mutation-adjacent tests when the target methods share the same repositories, contracts, or cache invalidation behavior.
- A minimal suggested test command when the target tests resolve cleanly to one test project or collection.

**Success criteria:**

- Suggested tests changed or run improves for refactor sessions that already have exact targets.
- New characterization-test opportunities are surfaced earlier for weakly shielded seams.
- `build_minimal_context`, `plan_context_workflow`, and refactor-oriented tool flows can consume the tighter ranked shield list instead of broad raw matches.
