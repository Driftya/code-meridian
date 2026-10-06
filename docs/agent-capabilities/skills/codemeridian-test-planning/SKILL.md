---
name: codemeridian-test-planning
description: Plan focused tests with CodeMeridian by finding relevant test shields, coverage gaps, impacted behavior, and the smallest useful test set before implementation.
---
# CodeMeridian Test Planning Skill

Use this skill when working in a repository indexed by CodeMeridian and the user asks to add, update, review, or plan tests.

The goal is to identify the smallest useful set of tests before changing behavior or adding new code.

## Target Scope

Resolve each supplied file/directory, including external paths, against its own Git/worktree or configured solution root. Read the nearest applicable ancestor `meridian.json` (`project`) within that root; nested configs may define distinct index roots. Honor explicit user/index-invocation context. Current-repository/global/environment defaults do not establish external ownership. Solution (`.sln`/`.slnx`), workspace, package and folder names are fallback candidates requiring bounded graph confirmation. Start new paths from the nearest existing parent and report unresolved scope instead of guessing.

Keep target -> index root -> project context, identity evidence and freshness. Use root-relative file hints and returned canonical IDs. A build-project/framework scope is separate from graph project context. Retain relevant unindexed external sources/tests as labeled evidence; discover through ancestors, not scans of sibling repositories. A reference path alone does not authorize external edits, indexing or build/test commands.

For shared libraries, use `find_cross_project_dependencies` and inspect relevant producer and consumer test shields in their own contexts. Preserve installed package version/framework and source status. `associated_current_source` consumers indicate potential compatibility tests; they are not verified callers of the checkout. Plan the smallest producer contract and consumer regression checks supported by the actual dependency, keeping commands tied to each owning solution. Missing, ambiguous, pending or stale associations and empty shields are coverage uncertainty, not proof of safety.

## SQL Coverage And Test Planning

For SQL behavior/schema changes, map project context separately from dialect, logical `databaseScope`, source-pattern settings and `searchPath`. SQL indexing is opt-in and currently parses PostgreSQL offline. Locate exact objects and file-owned declaration/reference sites with `query_codebase` / `resolve_exact_symbol`; check freshness and SQL file status, then use bounded context, `find_impact`, `find_downstream` and `find_connection` to identify readers/writers, views, explicit foreign keys and joins. These are generic graph tools; joins are not runtime calls and shared objects need not map to one file.

Use test-shield/coverage tools where graph links exist, but confirm relevant SQL/parser or consumer tests from narrowed source when they do not. Empty method/class shields do not prove SQL coverage or safe deletion. Missing/multiple search-path schemas, neutral undeclared relations, unresolved sites, partial analysis and stale retained facts need explicit uncertainty. Do not infer live database behavior, function-body coverage, migration ordering, dynamic SQL coverage or automatic linkage with legacy C#/TypeScript table identities.

Choose tests for the changed behavior: qualified/quoted identity and scope isolation; SELECT reads versus DML writes/source reads; views, joins or explicit foreign keys when touched; unresolved search paths and partial/parse-failure handling when relevant. For SQL indexer lifecycle changes, cover changed declarations updating unchanged consumers, replay without duplicate occurrences, deletion preserving still-used shared objects, failed publication/cache retries, and stale-edge exclusion from impact/connection. Prefer local parser fixtures; use isolated Neo4j only for persistence/traversal behavior, and database integration only when runtime SQL behavior is the actual task.

When modifying CodeMeridian itself, existing focused seams include `tools/SqlIndexer/tests`, `SqlIndexingTests`, `SqlGraphSnapshotTests` and `Neo4jSqlGraphIntegrationTests`. Keep commands and fixtures tied to their owning project; querying SQL graph context does not authorize running migrations.

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
* validate SQL dependency or schema changes

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

### Optional Human Reasoning Handoff

When the user explicitly wants interactive reasoning about which behavior,
invariant, or edge case the tests should encode, or their goal is learning
rather than immediate test implementation, hand the exact indexed target plus
verified source and test evidence to the `human-cognitive-seed` challenge
workflow. That skill owns challenge behavior.

Do not quiz the user about routine test additions. Do not start a challenge when
the target is fuzzy, stale, or unindexed; establish the exact target and evidence
first.

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
Project scopes: target -> index root -> projectContext (evidence / freshness)
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

