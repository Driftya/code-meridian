# Add Context-Pack Test Recommendation Pruning

- Status: implemented
- Priority: P2
- Note: `build_minimal_context` should reuse focused verification categories instead of a broad related-tests bucket.

**Problem:** The context pack still grouped tests into direct and heuristic matches even after `find_test_shield` and feature-path planning were narrowed. That left one of the highest-traffic tools with a noisier verification story than the newer test-planning surfaces.

**Goal:** Make `build_minimal_context` surface a smaller, clearer verification plan.

**Expected output:**

- Focused test categories aligned with `find_test_shield`.
- A minimal suggested test command when the recommended tests collapse cleanly enough and project analysis config declares a matching `analysis.testCommands.strategies[]` entry, or the legacy flat fallback.
- Heuristic tests still visible, but clearly secondary.

**Implemented:** `build_minimal_context` now renders its test section as focused verification categories: direct regression tests, contract/API forwarding tests, integration-level verification, and heuristic shield tests. It also emits a narrow suggested test command only when the non-heuristic candidates resolve cleanly enough to one seam and project analysis config declares a matching `analysis.testCommands.strategies[]` entry, or the legacy flat fallback.
