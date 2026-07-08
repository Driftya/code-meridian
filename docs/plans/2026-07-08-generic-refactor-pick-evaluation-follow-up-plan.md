# Generic Refactor-Pick Evaluation Follow-Up Plan

- Status: implemented
- Date: 2026-07-08
- Scope: improve generic trustworthiness of refactor-picking and validation tools after a live evaluation on a non-CodeMeridian codebase
- Principle: keep default behavior generic across arbitrary codebases; if a rule depends on local test runners, framework conventions, or project structure, it must be configurable or metadata-driven

## Why This Follow-Up Exists

The previous class-level caller precision plan improved `find_god_classes` and class-target `find_impact`, and that work materially helped on a live Driftya refactor pass.

However, the latest evaluation still exposed three generic gaps:

1. `find_impact` was trustworthy for one class target but still empty for other broad class targets that obviously matter.
2. `find_test_shield` still leaked helper methods, test doubles, and file/class containers into the shield output when only a few actual test cases mattered.
3. Suggested test commands can be low-confidence or over-broad, especially when the tool cannot prove the test runner contract.

These are reusable product issues, not Driftya-specific bugs.

## Evaluated Live Outcome

### What Worked

- `find_god_classes` successfully elevated a realistic application-layer target instead of just a raw line-count list.
- Caller-evidence buckets in `find_god_classes` were useful enough to support a refactor pick.
- `find_impact` successfully returned class-expanded callers for a class whose public behavior was exercised directly through tests.

### What Still Failed Or Felt Weak

- `find_impact` still returned `"No callers found"` for other broad class targets where the code was clearly operationally important.
- `find_test_shield` mixed real test cases with:
  - helper factory methods
  - test-double methods
  - file/class container nodes
  - nearby same-file implementation helpers
- Suggested test commands were sometimes too broad to be safely actionable.

## Goal

Improve the generic reliability of the refactor-picking flow:

1. `find_god_classes` should identify credible refactor candidates.
2. `find_impact` should provide enough caller evidence to trust or distrust the candidate.
3. `find_test_shield` should point to the smallest useful regression set.
4. Suggested test commands should only appear when the tool has enough confidence to recommend them.

## Non-Goals

- Do not hardcode Driftya class names, namespaces, test patterns, or framework conventions.
- Do not make the tools noisier by default just to avoid empty output.
- Do not invent fake certainty when graph evidence is weak.
- Do not bind command suggestions to one test framework unless the project configuration or indexed metadata says that runner is valid.

## Proposed Generic Improvements

### 1. Add A Second-Stage Class-Target Fallback In `find_impact`

Current behavior is strongest when callers reach a class through direct class-expanded member paths.

Remaining gap:

- Some broad classes still produce no callers even though they are operationally central.

Proposed generic direction:

- When class-target impact is empty after the current direct/member expansion, run a bounded fallback that can surface:
  - interface consumers
  - composition/dependency callers
  - constructor or factory usage
  - bounded workflow anchors already represented in the graph

Important rules:

- Fallback output must be labeled separately from direct structural callers.
- If no strong evidence exists, the tool should say so plainly instead of pretending the fallback is proven.

### 2. Split `find_test_shield` Into Test-Case Evidence Versus Support-Node Noise

Current shield output still over-counts nodes that are technically near tests but not themselves useful regression targets.

Proposed generic direction:

- Prioritize executable test-case methods first.
- Demote or suppress:
  - helper builders
  - same-file implementation helpers
  - test doubles
  - fixture/setup methods
  - file/class container nodes
- Preserve support-node visibility only in secondary diagnostics when it adds explanation value.

### 3. Make Suggested Test Commands Confidence-Gated

Current command suggestions can overreach when the system does not know enough about the repository's runner, naming rules, or selection granularity.

Proposed generic direction:

- Only emit concrete test commands when the command is backed by:
  - known repository configuration
  - indexed runner metadata
  - strong deterministic mapping from target node to runner selection syntax
- Otherwise:
  - omit the command, or
  - emit a clearly lower-confidence note instead of a concrete command

### 4. Add Runner And Suppression Behavior Through Configuration, Not Hardcoding

If behavior depends on local conventions, capture that through:

- `analysis.*` config
- indexed config metadata
- project/test-runner metadata

Examples:

- test command strategy
- preferred method/class/file granularity for shield suggestions
- whether same-file helper methods should be suppressible by default

## Review-Based Success Criteria

- [x] `find_impact` no longer returns empty class-target results in cases where bounded indirect evidence clearly exists.
- [x] `find_impact` still stays quiet when there is genuinely no useful caller evidence.
- [x] `find_test_shield` focuses the main output on real test-case methods when they exist.
- [x] helper/setup/test-double leakage is demoted out of the primary verification set by default.
- [x] suggested test commands only appear when command confidence is high enough to be actionable.
- [x] any runner-specific behavior is config- or metadata-driven.
- [x] repository and application tests cover the new generic contract.

## Implementation Seams

Primary seams to inspect or change:

- `src/Infrastructure/Graph/Neo4jCodeGraphRepository.Analytics.cs`
  - `FindImpactAsync(...)`
  - `FindImpactPathsAsync(...)`
- `src/Application/Services/CodebaseQueryService.Analytics.cs`
  - `FindImpactAsync(...)`
  - `FindTestShieldAsync(...)`
  - suggested test-command helpers
- analysis/config types if runner-aware or suppression-aware behavior needs opt-in configuration

Likely test seams:

- `tests/CodeMeridian.Infrastructure.Integration.Tests/Neo4jCodeGraphRepositoryIntegrationTests.cs`
- `tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs`

## Proposed Phases

### Phase 1. Lock The Remaining Gaps With Failing Tests

- [x] add repository tests for class targets where direct caller expansion is empty but bounded indirect evidence exists
- [x] add application tests for shield output that currently over-promotes helpers/test doubles
- [x] add application tests for low-confidence command-suggestion scenarios

### Phase 2. Improve Class-Target Fallback Evidence

- [x] add a bounded fallback evidence bucket for class-target `find_impact`
- [x] label fallback evidence separately from proven direct/member callers
- [x] keep the query bounded and confidence-aware

### Phase 3. Prune Shield Noise Generically

- [x] introduce a generic notion of executable test-case node versus support/test-helper node
- [x] re-rank or suppress support nodes in the focused verification plan
- [x] keep support-node visibility only in a secondary diagnostics section when useful

### Phase 4. Confidence-Gate Command Suggestions

- [x] add a confidence threshold for concrete test-command output
- [x] support config-driven command strategies where the project declares them
- [x] suppress command suggestions when the tool cannot justify them

### Phase 5. Documentation And Evaluation

- [x] update `docs/features.md` for `find_impact` and `find_test_shield`
- [x] document any new config knobs in `docs/indexing.md` or related config docs
- [ ] rerun a live evaluation against at least one non-CodeMeridian codebase and one internal synthetic fixture set

## Risks

- too much fallback broadening in `find_impact` could reintroduce noisy caller lists
- too aggressive shield pruning could hide useful integration-style tests in sparse codebases
- command suppression could feel like regression unless the tool explains why it withheld the command
- adding runner-aware behavior without a clean config boundary would drift back toward repo-specific logic

## Definition Of Done

- [x] refactor-pick support is more trustworthy on arbitrary codebases, not just those with direct test-to-method call paths
- [x] `find_test_shield` is smaller and more actionable by default
- [x] test-command output is confidence-aware and does not over-claim runner compatibility
- [x] repo/framework-specific behavior stays in configuration or indexed metadata
