# Add Precision Feedback Loop

- Status: implemented
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

## Implemented

- `evaluate-session` now emits a summarized `.meridian/precision-feedback.json` file with per-tool accepted and ignored file/test signals.
- `find_implementation_surface` consumes that feedback to boost historically accepted files, penalize historically ignored files, and explain those adjustments inline.
- `analyze_feature_implementation_path` consumes the same feedback and appends ranking reasons when prior sessions repeatedly accepted a surface.
- The feedback remains explainable: output reasons call out accepted/ignored history plus prior file-only, heuristic, or stale pressure instead of hiding the adjustment behind an opaque score.

## Follow-up Planning

- Status: pending

- Tighten session-evidence guidance so non-trivial implementation sessions record the exact narrowing path, not only the initial feature-mapping step and final test runs.
- Add a detailed follow-up slice for session-evidence capture quality with these expectations:
  - After feature mapping, record `resolve_exact_symbol` for the real .NET runner, class, or method that will be edited.
  - Record `get_context_for_editing` or `build_minimal_context` before editing so the bounded context step is visible to `evaluate-session`.
  - Record `find_test_shield` on that exact target, then run or update at least one of the suggested tests when the test seam is relevant.
  - If implementation pivots away from a heuristic feature suggestion, record the exact follow-up graph call that narrowed the target, rather than only the original heuristic call.
- Improve the recommended JSONL evidence pattern so sessions that use exact-target reasoning throughout the work do not look artificially weak just because only a few events were written.
- Revisit `evaluate-session` scoring once the richer evidence pattern is in place, so scaffolding sessions are judged on actual graph-assisted narrowing rather than under-recorded transcripts.
