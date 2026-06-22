# Add Tool Dependency Impact Matrix

- Status: implemented
- Priority: P2
- Note: When one CodeMeridian tool changes, contributors need a deterministic way to see which other tools, reports, evaluators, and tests may also need review.

**Problem:** Tool coupling in CodeMeridian is often semantic rather than compile-time. A change to planning, ranking, confidence labels, warnings, or output shape can silently affect CLI reports, MCP wrappers, evaluation flows, and other higher-level tools even when the build stays green.

**Goal:** Make cross-tool dependencies explicit so feature and refactor work can verify the right adjacent surfaces instead of relying on memory.

**Expected output:**

- A maintained dependency matrix that maps producer tools to downstream consumers.
- Clear contract types such as output shape, warning semantics, ranking behavior, session evidence, and shared helper logic.
- Required regression suites or smoke checks for each dependency edge.
- A simple review workflow for "if this tool changes, also inspect these tools/docs/tests".

**Suggested scope:**

- Cover MCP tools, CLI report commands, evaluation/session tooling, and shared application services.
- Distinguish hard dependencies from softer awareness-only consumers.
- Keep the matrix human-readable first; CI automation can consume it later.

**Initial examples:**

- `plan_context_workflow` -> `execute_context_workflow`, MCP tool docs, report/evaluation tooling that interprets workflow warnings or step shape
- `find_test_shield` -> `build_minimal_context`, PR context reports, refactor/planning workflows
- `find_related_knowledge` -> context packs, PR reports, documentation-oriented workflows
- session evidence format -> `evaluate-session` and any CI/reporting surfaces that summarize usefulness

**Implemented:** Added `find_tool_dependency_impact` as a first-class CodeMeridian query tool. The initial implementation uses an explicit application-side dependency catalog instead of persisted Neo4j nodes, and it can return either the full tracked matrix or the upstream/downstream impact for one tool, report, evaluator, or shared contract. The output includes hard versus awareness-only dependency edges plus the regression suites and docs to review for each tracked coupling. The first catalog covers workflow planning/execution, test-shield and context-pack alignment, PR context reporting, session evidence evaluation, and precision-feedback consumers.
