# Add Test Target Precision

- Status: implemented
- Priority: P2
- Note: Suggest the tests most likely to change or run, not every loosely related shield.

**Problem:** Test-shield and feature-path tools can surface many related tests, but not all of them are useful for the immediate implementation slice. In the architecture erosion timeline session, only 1 of 6 suggested tests was actually changed or run.

**Goal:** Improve test recommendations so agents get a small verification plan instead of a broad shield map.

**Expected output:**

- Separate direct regression tests, contract/API forwarding tests, integration tests, and heuristic shield tests.
- Rank tests by proximity to the selected edit-ready targets.
- Prefer tests in the same application/MCP/infrastructure layer as the changed files.
- Include a minimal recommended test command when enough project metadata is known.

**Success criteria:**

- Suggested tests changed or run improves across evaluated sessions.
- Heuristic tests are still visible but no longer mixed with primary verification targets.
- Recommendations explain whether a test is direct, contract-level, integration-level, or heuristic.

**Implemented:** `find_test_shield` now adds a focused verification plan that separates direct regression tests, contract/API forwarding tests, integration-level verification, and heuristic shield tests while keeping the existing shield-map sections. `analyze_feature_implementation_path` now uses the same categories for its test plan and emits a minimal suggested test command only when project analysis config provides a high-confidence `analysis.testCommands.strategies[]` match, or the legacy flat fallback, and the recommended tests collapse cleanly enough to target one seam.
