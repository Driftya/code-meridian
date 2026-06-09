# CodeMeridian Usage Guide

CodeMeridian works best when the AI assistant uses it before editing.

The goal is not to make the assistant read more files.
The goal is to make it read the right context, check whether that context is trustworthy, and make smaller, safer changes.

CodeMeridian is the map.
The coding assistant still makes the change.

## Recommended AI Workflow

Use this flow for non-trivial changes:

1. Understand the goal.
2. Ask CodeMeridian for the likely implementation surface.
3. Resolve exact symbols before editing.
4. Check graph freshness or drift.
5. Build a minimal context pack.
6. Make the smallest change that satisfies the goal.
7. Run tests or explain what could not be verified.

## General Prompt Template

Use this when asking an AI assistant to work with CodeMeridian:

```text
Use CodeMeridian before editing.

Goal:
<describe the change>

Rules:
- State assumptions before changing code.
- Use CodeMeridian to find the implementation surface.
- Resolve exact symbols where possible.
- Check graph freshness or drift before trusting graph results.
- Build a minimal context pack for the main target.
- Make surgical changes only.
- Do not refactor unrelated code.
- Run relevant tests or explain what could not be verified.
```

## Prompt: Understand the Architecture

```text
Use CodeMeridian to give me an architectural overview of this project.

Focus on:
- main namespaces or modules
- key interfaces and implementations
- dependency direction
- possible architecture violations
- areas that look risky to change

Do not edit code.
```

## Prompt: Prepare for a Feature Change

```text
Use CodeMeridian to prepare for this feature:

<feature description>

Before editing:
1. Find the likely implementation surface.
2. Resolve exact symbols where possible.
3. Check graph freshness or drift.
4. Identify direct callers, callees, and related tests.
5. Build a minimal context pack.
6. Tell me the planned files to change and why.

Do not edit until the plan is clear.
```

## Prompt: Find the Blast Radius

```text
Use CodeMeridian to find the impact of changing:

<method, class, endpoint, or file>

Include:
- direct callers
- transitive impact if available
- downstream dependencies
- related tests
- architecture risks
- any stale or heuristic results

Do not edit code.
```

## Prompt: Build Minimal Context Before Editing

```text
Use CodeMeridian to build a minimal context pack for:

<target symbol or file>

Include:
- target metadata
- direct callers
- direct callees
- interfaces and implementations
- related tests
- coverage gaps if available
- likely edit files
- token estimate
- graph freshness confidence

After that, summarize what context is safe to trust.
```

## Prompt: Check Stale Knowledge

```text
Use CodeMeridian to find stale knowledge for this project.

Look for:
- documentation that mentions missing or renamed code
- orphaned external concepts
- stale notes
- code references that no longer resolve
- graph drift that should trigger re-indexing

Do not edit code.
```

## Prompt: Fix a Bug Safely

```text
Use CodeMeridian before fixing this bug:

<bug description>

Process:
1. State assumptions.
2. Find the likely implementation surface.
3. Locate related tests.
4. Check graph freshness.
5. Propose the smallest fix.
6. Add or update a test if appropriate.
7. Avoid unrelated refactoring.

After the change, explain what was verified.
```

## Prompt: Add Tests Around Existing Code

```text
Use CodeMeridian to find test coverage gaps around:

<target feature, class, or method>

Then:
- identify existing nearby tests
- identify production paths with weak or missing test coverage
- suggest the smallest useful tests
- add tests without changing production behavior unless required
```

## Prompt: Review a Planned Change

```text
Use CodeMeridian to review this planned change:

<describe planned change>

Check:
- whether the proposed files are the right implementation surface
- what callers and downstream dependencies may be affected
- whether tests exist nearby
- whether architecture boundaries are respected
- whether the graph is fresh enough to trust

Do not edit code.
```

## Prompt: Architecture Boundary Check

```text
Use CodeMeridian to check architecture boundaries.

Look for:
- Domain depending on Infrastructure or Presentation
- Application depending on concrete Infrastructure types
- circular dependencies
- framework types leaking into inner layers
- repositories exposing persistence-specific abstractions

Return actionable findings only.
```

## Prompt: Before Deleting Code

```text
Use CodeMeridian before deleting:

<symbol, file, or feature area>

Check:
- direct and transitive references
- tests that still call it
- documentation that mentions it
- external concepts or endpoints linked to it
- stale graph risk

Do not delete anything unless the result is low risk.
```

## Prompt: Explain Why a File Is Relevant

```text
Use CodeMeridian to explain why this file is relevant:

<file path>

Show:
- incoming relationships
- outgoing relationships
- nearby tests
- related docs
- paths from important entry points if available
- whether the graph result is exact, heuristic, or stale
```

## Prompt: Use CodeMeridian During Refactoring

```text
Use CodeMeridian to plan this refactor:

<refactor goal>

Before editing:
1. Find affected modules and symbols.
2. Identify bridge or high-impact nodes.
3. Find tests that protect the current behavior.
4. Check graph freshness.
5. Suggest an ordered refactor plan.
6. Keep each step small and verifiable.

Do not perform broad cleanup outside the refactor goal.
```

## Prompt: Re-index Decision

```text
Use CodeMeridian to decide whether I should re-index before continuing.

Check:
- graph drift
- missing files
- invalid line ranges
- stale timestamps
- confidence of matching nodes

Return:
- re-index recommended: yes/no
- reason
- risk level
```

## Good Assistant Behavior

When using CodeMeridian, the assistant should:

* ask when requirements are ambiguous
* state assumptions
* prefer exact graph matches over heuristic matches
* mention stale or low-confidence results
* avoid unrelated edits
* keep changes small
* run relevant tests where possible
* explain what was verified

## Bad Assistant Behavior

Avoid prompts that allow the assistant to:

* edit before checking impact
* trust stale graph results silently
* refactor unrelated code
* remove code only because it looks unused
* ignore tests
* make broad architecture changes without a plan

## Minimal Daily Prompt

For everyday work, this short prompt is usually enough:

```text
Use CodeMeridian before editing. Find the implementation surface, check freshness, build a minimal context pack, then make the smallest safe change. Avoid unrelated refactoring and verify with tests where possible.
```
