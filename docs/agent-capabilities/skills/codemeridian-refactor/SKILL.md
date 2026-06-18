---
name: codemeridian-refactor
description: Plan safer refactors with CodeMeridian by checking graph freshness, exact symbols, impact, tests, duplication, and architecture risk before editing.
---
# CodeMeridian Refactor Skill

Use this skill when working in a repository indexed by CodeMeridian and the user asks to refactor, rename, split, extract, move, simplify, deduplicate, delete, or replace code.

The goal is to make refactoring safer by using graph context before changing files.

## When To Use

Use this skill when the request includes words or intent like:

* refactor this
* clean this up
* split this class
* extract a service
* extract an interface
* rename this
* move this code
* remove duplication
* delete unused code
* simplify this flow
* replace this implementation
* reduce coupling
* improve architecture
* make this more maintainable

## Core Rule

Do not start a refactor from a single file only.

First identify:

1. what the code is
2. who calls it
3. what it calls
4. which tests protect it
5. what graph data is stale or uncertain
6. which edits are safe versus risky

## Workflow

### 1. Check Graph Freshness

Before trusting exact targets, check whether the graph matches the working tree.

Prefer:

* `check_graph_freshness`
* `find_graph_drift`

Report freshness clearly:

```text
Graph freshness: fresh / stale / unknown
```

If freshness is stale or unknown, use graph results as guidance only and confirm exact files manually.

### 2. Resolve Exact Refactor Target

If the user names a class, method, endpoint, interface, component, or file, resolve it before editing.

Prefer:

* `resolve_exact_symbol`
* `get_context_for_editing`

Avoid refactoring by fuzzy name match when exact symbol resolution is available.

Report:

```text
Refactor target:
- Symbol:
- File:
- Confidence:
```

### 3. Build Minimal Context

Gather the smallest useful context pack.

Prefer:

* `build_minimal_context`
* `find_implementation_surface`
* source snippets with strict budgets when available

Do not load large unrelated files unless CodeMeridian cannot answer the question.

Report why each file matters when possible:

```text
Included because:
- direct target
- caller
- dependency
- test
- configuration
- diagnostic
- documentation match
```

### 4. Inspect Impact

Before changing signatures, moving code, deleting code, or replacing behavior, inspect blast radius.

Prefer:

* `find_impact`
* `find_connection`
* `find_unreferenced` before deleting code
* `find_bridge_nodes` when available
* `plan_change_route` when available

Separate proven impact from inferred impact.

Use labels:

* Direct caller
* Direct dependency
* Transitive dependency
* Test dependency
* Documentation dependency
* Inferred relationship
* Unknown

### 5. Inspect Test Shield

Before behavior changes, identify tests that protect the target.

Prefer:

* `find_test_shield`
* `find_coverage_gaps`

Report:

```text
Tests to inspect or run:
- Existing:
- Missing:
- Weak coverage:
```

If no relevant tests exist, recommend the smallest useful test set before or alongside the refactor.

### 6. Inspect Duplication And Extraction Signals

When the user asks to remove duplication, extract code, or split a large type, inspect similarity and cohesion.

Prefer:

* `find_duplicate_candidates`
* `find_similar_nodes`
* `find_large_nodes`
* `find_god_classes`
* `find_refactor_extraction_candidates` when available
* `find_natural_modules` when available

Do not extract only because code looks similar. Check whether the code has the same reason to change.

Report:

```text
Extraction signal:
- Strong / medium / weak
- Reason:
- Risk:
```

### 7. Check Architecture Risk

Before moving code between layers or extracting abstractions, check dependency direction and boundary rules.

Flag risks such as:

* Domain depending on Infrastructure, Presentation, EF Core, HTTP, logging, or configuration
* Application depending on concrete Infrastructure types
* Infrastructure leaking EF Core types or `IQueryable`
* business rules inside UI, controllers, endpoints, or ViewModels
* circular dependencies
* shared abstractions placed in the wrong layer
* framework concerns leaking into Domain

For Onion Architecture, prefer:

```text
Presentation -> Application -> Domain
Infrastructure -> Application
```

Domain must stay framework-free.

### 8. Create A Safe Edit Route

Before editing, produce a small refactor plan.

Use this format:

```text
Safe edit route:
1. Add or adjust tests.
2. Introduce new abstraction/type/method without changing behavior.
3. Move logic behind the new boundary.
4. Update call sites.
5. Remove old code only after tests pass.
6. Run targeted tests.
```

For risky refactors, split the plan into separate commits.

### 9. Preserve Behavior First

Prefer behavior-preserving refactors unless the user explicitly requests behavior change.

If behavior changes are needed, state them separately:

```text
Behavior-preserving changes:
- ...

Behavior-changing changes:
- ...
```

### 10. Final Report Before Editing

Before implementing, summarize:

```text
Graph freshness:
Refactor target:
Minimal context:
Impact:
Tests:
Architecture risks:
Safe edit route:
Unknowns:
```

Then continue with the requested refactor.

## Refactor Guardrails

### Do

* Prefer small, reversible steps.
* Preserve public API contracts unless asked otherwise.
* Keep existing behavior stable.
* Add tests before risky movement.
* Use dependency inversion for new seams.
* Keep domain rules in Domain.
* Keep orchestration in Application.
* Keep persistence and external adapters in Infrastructure.
* Keep UI state and navigation in Presentation.
* Use async APIs with `CancellationToken` for I/O.
* Keep logging structured and loop-safe.

### Do Not

* Start with a broad repository scan if graph tools are available.
* Move business rules into controllers, endpoints, UI, or infrastructure.
* Introduce circular dependencies.
* Expose EF Core types or `IQueryable` outside Infrastructure.
* Replace abstractions with concrete infrastructure dependencies.
* Delete code before checking references.
* Rename public members without checking callers.
* Change behavior silently.
* Hide stale graph uncertainty.
* Ignore missing tests.

## Output Template

Use this template when reporting a refactor plan:

```text
Graph freshness:
- Status:
- Notes:

Refactor target:
- Symbol/file:
- Confidence:

Minimal context:
- Files/symbols:
- Why included:

Impact:
- Direct callers:
- Dependencies:
- Transitive or inferred risk:

Tests:
- Existing tests:
- Missing tests:
- Suggested targeted test command:

Architecture risks:
- Boundary risks:
- Dependency risks:
- Logging/async/nullability risks:

Safe edit route:
1.
2.
3.

Unknowns:
- 
```

## Failure Mode

If CodeMeridian cannot provide enough context:

1. Say which graph query failed or returned insufficient data.
2. Fall back to narrow repository search.
3. Avoid wide file dumps.
4. Recommend re-indexing if graph freshness is stale.
5. Continue only with clearly stated assumptions.

