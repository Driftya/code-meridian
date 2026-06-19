---
name: codemeridian-test-planning
description: Plan focused tests with CodeMeridian by finding relevant test shields, coverage gaps, impacted behavior, and the smallest useful test set before implementation.
---
# CodeMeridian Test Planning Skill

Use this skill when working in a repository indexed by CodeMeridian and the user asks to add, update, review, or plan tests.

The goal is to identify the smallest useful set of tests before changing behavior or adding new code.

projectContext can be found in meridian.json in field project.

## When To Use

Use this skill when the request includes words or intent like:

* add tests
* update tests
* fix failing tests
* what tests should I run
* what tests cover this
* improve coverage
* find missing tests
* test this feature
* test this bug fix
* validate this refactor
* behavior changed
* check regression risk
* make CI safer
* review test impact

Also use this skill before implementing behavior changes when test coverage is unclear.

## Core Rule

Do not guess test coverage from filenames alone.

First identify:

1. the behavior being changed
2. the exact symbols or files involved
3. the tests already connected to that behavior
4. the gaps where no tests protect the behavior
5. the smallest test set that gives useful confidence

## Workflow

### 1. Check Graph Freshness

When exact test relationships matter, check whether the graph matches the working tree.

Prefer:

* `check_graph_freshness`
* `find_graph_drift`

Report freshness clearly:

```text id="h2cnms"
Graph freshness: fresh / stale / unknown
```

If freshness is stale or unknown, use graph results as guidance only and verify exact files manually.

### 2. Identify The Behavior Under Test

Clarify the behavior in implementation terms.

Prefer:

* `analyze_feature_implementation_path` when testing a feature request or `docs/features/*.md`
* `build_minimal_context`
* `find_implementation_surface`
* `resolve_exact_symbol`
* `get_context_for_editing`

Report:

```text id="rwfm66"
Behavior under test:
- Feature / bug / refactor:
- Main symbols:
- Main files:
- Expected behavior:
```

If the behavior is unclear, state the assumption and continue with the safest likely interpretation.

### 3. Find The Existing Test Shield

Find tests already connected to the target behavior.

Prefer:

* `find_test_shield`
* `find_impact`
* `find_connection`

Group test candidates by confidence:

```text id="bh0wcm"
Existing test shield:
- High confidence:
- Medium confidence:
- Low confidence / inferred:
```

High confidence means the graph directly links the test to the target symbol, file, endpoint, or dependency path.

Medium confidence means the test is nearby by module, naming, or dependency path.

Low confidence means the relationship is inferred and should be manually checked.

### 4. Find Coverage Gaps

Look for behavior that is not protected by tests.

Prefer:

* `find_coverage_gaps`
* `find_unreferenced` when verifying unused or deletion-related behavior
* `find_duplicate_candidates` or `find_similar_nodes` when duplicated behavior might need shared tests

Report gaps as behavior, not only files:

```text id="sq0uyz"
Coverage gaps:
- Missing domain invariant test:
- Missing application use case test:
- Missing infrastructure adapter test:
- Missing API/endpoint test:
- Missing regression test:
```

### 5. Choose The Smallest Useful Test Set

Prefer focused tests over broad test storms.

Use this order:

1. Domain tests for pure business rules and invariants.
2. Application tests for use cases, orchestration, authorization decisions, and port interaction.
3. Infrastructure tests for persistence, external adapters, mappings, migrations, and query behavior.
4. Presentation/API tests for routing, authorization, model binding, response contracts, and error translation.
5. End-to-end tests only when the behavior crosses boundaries and cannot be trusted through smaller tests.

Report:

```text id="srfl8e"
Smallest useful test set:
1.
2.
3.
```

### 6. Respect Architecture Boundaries

When proposing tests, keep the tested concern in the right layer.

For Onion Architecture:

```text id="bem57u"
Presentation -> Application -> Domain
Infrastructure -> Application
```

Testing guidance:

* Test Domain rules with real domain objects.
* Test Application use cases with mocked or fake ports.
* Test Infrastructure with real persistence or adapter test doubles where appropriate.
* Test Presentation/API behavior through endpoint/page/controller tests.
* Do not mock domain entities.
* Do not put business rule tests only in UI tests.
* Do not test EF Core behavior through Application mocks.

### 7. Plan Test Data

Prefer deterministic, readable test data.

Use:

* explicit Arrange / Act / Assert structure
* real domain entities
* fake clocks or time providers
* stable IDs when needed
* realistic edge cases
* async tests for async code

Avoid:

* `Thread.Sleep`
* random data without a fixed seed
* testing private methods directly
* brittle string matching unless the string is the contract
* large fixture blobs when a small object builder is clearer

### 8. Plan Commands To Run

Recommend targeted commands before broad commands.

Examples:

```bash id="x60gd5"
dotnet test tests/CodeMeridian.Application.Tests/CodeMeridian.Application.Tests.csproj --filter FullyQualifiedName~Context
```

```bash id="wyfwal"
dotnet test
```

Use the narrowest useful command first, then broader CI-style commands if needed.

If the repo uses frontend tests, include the relevant package script only when the changed behavior touches frontend code.

### 9. Handle Failing Tests

When the task is to fix failing tests:

1. Identify whether the failure is behavior regression, stale test expectation, environment issue, or graph/index drift.
2. Find the implementation and test relationship.
3. Prefer fixing the implementation when the test describes valid behavior.
4. Update the test only when the product contract intentionally changed.
5. Do not weaken assertions just to make tests pass.

Report:

```text id="i8a0oq"
Failure classification:
- Regression:
- Stale expectation:
- Environment:
- Unknown:
```

### 10. Final Report Before Writing Tests

Before writing or editing tests, summarize:

```text id="w1nxhf"
Graph freshness:
Behavior under test:
Existing test shield:
Coverage gaps:
Smallest useful test set:
Suggested test files:
Commands to run:
Risks / unknowns:
```

Then continue with the requested test implementation or review.

## Test Planning Guardrails

### Do

* Prefer behavior-focused tests.
* Keep tests close to the layer they validate.
* Use real domain objects.
* Mock or fake Application ports, not domain entities.
* Use async tests for async APIs.
* Pass `CancellationToken` when testing async use cases.
* Use deterministic time through a clock/time provider.
* Add regression tests for fixed bugs.
* Keep assertions specific enough to catch real regressions.
* Run targeted tests before broad test suites.

### Do Not

* Guess coverage from filenames alone.
* Add broad slow tests when a focused unit or application test is enough.
* Mock domain entities.
* Test private methods directly.
* Use `Thread.Sleep`.
* Hide missing coverage.
* Weaken assertions to make tests pass.
* Put business rule coverage only in UI/API tests.
* Ignore graph freshness when exact test mapping matters.
* Claim inferred coverage is proven coverage.

## Output Template

Use this template when reporting a test plan:

```text id="k18ha7"
Graph freshness:
- Status:
- Notes:

Behavior under test:
- Summary:
- Main symbols/files:

Existing test shield:
- High confidence:
- Medium confidence:
- Low confidence / inferred:

Coverage gaps:
- Domain:
- Application:
- Infrastructure:
- Presentation/API:

Smallest useful test set:
1.
2.
3.

Suggested test files:
- Existing files to update:
- New files to create:

Commands to run:
- Targeted:
- Broader:

Risks / unknowns:
-
```

## Failure Mode

If CodeMeridian cannot provide enough test context:

1. Say which graph query failed or returned insufficient data.
2. Fall back to narrow repository search.
3. Inspect nearby test project structure and naming conventions.
4. Avoid broad file dumps.
5. Recommend re-indexing if graph freshness is stale.
6. Continue only with clearly stated assumptions.

