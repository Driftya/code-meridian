# Add Derived Edit-Surface Credit For Extraction Refactors

- Status: implemented
- Priority: P1
- Note: Make `evaluate-session` fairer for extraction-heavy refactors and namespace/file regrouping work.

**Problem:** `codemeridian evaluate-session` currently compares suggested files with changed files mostly by direct path overlap. That works for narrow edits, but it under-credits sessions where CodeMeridian correctly identified the exact starting target and the implementation then split that target into new collaborator files, moved code into a clearer namespace, or created tests for the extracted seam. In those cases the graph assistance was still useful, but the evaluator can report weak suggested-file precision because the final edited files did not exist when the initial graph call ran.

Typical examples:

- `resolve_exact_symbol` and `get_context_for_editing` correctly point at a large class such as `ChainLifecycleService`.
- The implementation extracts `ChainHandoffService` or `ChainPassSuppressionService` into new files under the same bounded feature area.
- The evaluator sees the original file plus several newly created files and scores the session as only partially matching suggested files.

**Goal:** Let session evaluation recognize when new or moved files are derived from the suggested surface rather than treating them as unrelated edits.

**Expected output:**

- `evaluate-session` can award derived-match credit when changed files are clearly extracted from a suggested exact target, responsibility slice, or planned namespace/folder.
- Session evidence can optionally record narrowing and extraction lineage instead of only raw suggested file lists.
- Usefulness output stays explainable: direct matches and derived matches are reported separately.

**Success criteria:**

- A refactor that starts from an exact suggested class and extracts new collaborator files under the same feature slice is not scored the same as an unrelated edit storm.
- Output distinguishes:
  - direct suggested-file matches
  - derived matches from extraction or regrouping
  - unrelated changed files
- Precision feedback can preserve the difference between "this file was edited directly" and "this new file was created from this suggested target".
- The evaluator remains provider-neutral and does not require hidden editor metadata.

## Suggested design

1. Extend session evidence guidance with optional lineage fields such as:
   - `derivedFromFiles`
   - `derivedFromSymbols`
   - `plannedNamespaces`
   - `plannedFolders`
   - `changeKind` with values like `extract`, `move`, `rename`, `split`, `direct-edit`
2. Teach `evaluate-session` to compute derived matches using explainable heuristics:
   - changed file is new and lives under the same feature folder as the exact suggested target
   - changed file namespace matches a planned responsibility slice
   - changed file was recorded as derived from an exact symbol or suggested file
   - rename/move pairs preserve a strong path or symbol lineage
3. Report both direct and derived credit in the usefulness summary.
4. Feed derived-match history into `.meridian/precision-feedback.json` without conflating it with direct-hit precision.

## Implemented

- `evaluate-session` now separates direct suggested-file matches from derived lineage credit and unrelated changed files.
- The evaluator credits derived work when:
  - git rename or copy metadata preserves a suggested source path
  - session evidence records `derivedFromFiles`
  - session evidence records `plannedFolders` or `plannedNamespaces` for a suggested source and the changed file lands inside that planned slice
- `.meridian/precision-feedback.json` now keeps direct accepted file counts separate from derived accepted file counts and stores derived target paths per suggested source.
- `docs/evaluate.md` and the repo `codemeridian-context` skill now document the optional lineage fields for extraction and regrouping sessions.
- Added focused CLI evaluator tests for explicit extraction lineage, planned-namespace lineage, rename lineage, and precision-feedback persistence.

## Why this matters

- It makes the usefulness score better aligned with real refactor work.
- It reduces false negatives for graph-assisted extraction sessions.
- It encourages better evidence capture without forcing agents to predict every destination file before the refactor starts.
- It complements existing precision-feedback work instead of replacing it.

## Adjacent areas

- `docs/features/17-add-session-usefulness-evaluation.md`
- `docs/features/39-add-precision-feedback-loop.md`
- `docs/features/42-add-responsibility-slice-suggestions.md`
- `docs/evaluate.md`
