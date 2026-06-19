---
name: codemeridian-context
description: Gather minimal, graph-grounded CodeMeridian context before implementation, refactoring, deletion, debugging, or test planning.
---
# CodeMeridian Context Skill

Use this skill when working in a repository indexed by CodeMeridian.

The goal is to gather the smallest useful context pack before reading many files or making code changes.

projectContext can be found in meridian.json in field project.

## When To Use

Use this skill before:

* implementing a non-trivial change
* planning or starting a feature from a request or `docs/features/*.md`
* refactoring code
* deleting code
* changing public APIs
* debugging unfamiliar behavior
* reviewing impact
* planning tests
* modifying architecture-sensitive code

## Workflow

### 1. Check Graph Freshness

When exact file targets, symbol names, or relationships matter, check whether the graph is fresh.

Prefer:

* `check_graph_freshness`
* `find_graph_drift`

If the graph is stale, incomplete, or uncertain, say so before relying on exact results.

### Session Evidence

During an implementation session, always record provider-neutral session evidence under `.meridian/sessions/*.jsonl` so `codemeridian evaluate-session` can measure whether CodeMeridian helped.

Write one compact JSON object per line. Omit fields that do not apply.

Use this event shape:

```json
{"timestamp":"<ISO-8601 UTC time>","provider":"<codex|copilot|claude|continue|other>","project":"MyApp","kind":"<graph-call|codemeridian-tool|suggestion|tool-result|command|manual-fallback|test-run|stale-warning>","toolName":"<CodeMeridian MCP tool name when applicable>","command":"<shell command when applicable>","targetConfidence":"<exact|file-only|heuristic|stale, comma-separated if needed>","staleWarning":<true|false>,"contextPackStatus":"<full|degraded|failed when recording build_minimal_context results>","files":["<repo-relative file path>"],"tests":["<repo-relative test file path>"]}
```

For each CodeMeridian tool call, record `kind=graph-call`, `toolName`, files suggested by the tool, tests suggested by the tool, `targetConfidence`, and `staleWarning` when present.

When recording a `build_minimal_context` result as a `tool-result` event, also record `contextPackStatus` as `full`, `degraded`, or `failed`.

For manual search fallback commands such as `rg`, `grep`, `find`, `Get-ChildItem`, or `Select-String`, record `kind=command` and `command`.

For test execution, record `kind=test-run`, `command`, and `tests` when known.

### 2. Map Feature Implementation Path

When the task is feature work, start by mapping the implementation path.

Prefer:

* `analyze_feature_implementation_path` for a feature request or `docs/features/*.md`

Use the result to report:

* whether the feature is already documented or linked
* closest code areas
* likely touched services, repositories, endpoints, and tools
* tests and docs to update
* missing graph evidence
* risk level and confidence

### 3. Build Minimal Context

Use the smallest CodeMeridian query that fits the task.

Prefer:

* `build_minimal_context` for broad implementation tasks
* `find_implementation_surface` for exact feature/fix targets after the feature path is mapped
* `resolve_exact_symbol` before editing a named class, method, interface, endpoint, or file
* `get_context_for_editing` when preparing a focused edit

Avoid loading large unrelated files unless CodeMeridian cannot answer the question.

### 4. Check Impact And Tests

Before behavior changes, refactors, deletions, or signature changes, inspect risk.

Prefer:

* `find_impact`
* `find_test_shield`
* `find_coverage_gaps`
* `find_unreferenced` before deleting code

### 5. Use Documentation Context

When the task depends on prior decisions, architecture notes, or product behavior, search indexed documentation.

Prefer:

* `search_documentation`
* `find_related_knowledge`
* `find_stale_knowledge` when remembered knowledge may be outdated

### 6. Report Confidence

Separate proven graph facts from inferred relationships.

Use wording like:

* "The graph directly links..."
* "The graph suggests..."
* "This is inferred from..."
* "The graph may be stale because..."

Do not present stale or inferred context as certain.

## Output Format

Start with this compact summary before implementation:

```text
Graph freshness:
Minimal context:
Likely edit surface:
Tests to inspect or run:
Risks / unknowns:
```

Then continue with the requested implementation, review, or explanation.

## Guardrails

* Prefer CodeMeridian graph lookup before broad manual scanning.
* Prefer source snippets and detail levels before loading whole files.
* Do not return huge context dumps by default.
* Do not trust exact graph results when freshness is low.
* Do not ignore missing tests or weak coverage signals.
* Do not leak secrets, tokens, or private data into logs, prompts, or summaries.

## Failure Mode

If CodeMeridian cannot answer the task:

1. Say what was missing.
2. Fall back to normal repository search.
3. Keep the search narrow.
4. Recommend re-indexing if the graph appears stale.

