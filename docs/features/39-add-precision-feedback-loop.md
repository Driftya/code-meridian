# Add Precision Feedback Loop

- Status: pending
- Priority: P1
- Note: Use session evaluation results to improve future target ranking.

**Problem:** Session evaluation can already show when CodeMeridian suggested too many files or tests, but that evidence is not fed back into targeting. In the architecture erosion timeline session, CodeMeridian was useful but broad: 4 of 12 suggested files were edited and 1 of 6 suggested tests was changed or run.

**Goal:** Turn evaluated sessions into ranking signals for future graph tools.

**Expected output:**

- Track accepted, ignored, stale, and manually verified targets from session evidence.
- Prefer files and tests that were repeatedly accepted for similar feature/tool concepts.
- Downrank stale, file-only, and heuristic targets when exact targets exist nearby.
- Explain when feedback influenced ranking.

**Success criteria:**

- `evaluate-session` output can identify target precision regressions.
- `find_implementation_surface` and `analyze_feature_implementation_path` can consume summarized precision signals.
- Recommendations stay explainable and do not hide behind opaque scoring.
