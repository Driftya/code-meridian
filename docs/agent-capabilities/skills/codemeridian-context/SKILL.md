---
name: codemeridian-context
description: Gather minimal, graph-grounded CodeMeridian context before code changes, debugging or test planning, resolving target-folder project contexts and relevant cross-repository dependencies.
---
# CodeMeridian Context Skill

Use this skill when working in a repository indexed by CodeMeridian.

The goal is to gather the smallest useful context pack before reading many files or making code changes.

## Resolve Project Context From The Target

Resolve scope before querying the graph. Start from each user-supplied file's parent or target directory, rather than assuming the current working directory owns every target.

1. Resolve the absolute path and owning Git/worktree root (`git -C <directory> rev-parse --show-toplevel` when available). A `.git` file also marks a worktree/submodule boundary. For a new path, start from its nearest existing parent. For a non-Git directory, use the enclosing configured project or solution/workspace root.
2. Inspect the nearest applicable `meridian.json` on that ancestor chain, including the owning root, and read its `project`. Do not continue into an unrelated parent repository. A nested config may define a separately indexed subproject inside the same Git repository.
3. Honor a project context explicitly supplied for that target by the user or established by its actual index invocation. Global configuration, an inherited `CodeMeridian_Project`, or the current repository's config is not evidence that an external target belongs to that context. If local identity is missing, solution (`.sln`/`.slnx`), workspace, package and root-folder names are candidates; confirm with a bounded graph lookup of a known file/symbol and its returned `projectContext`. State unresolved ambiguity instead of silently selecting a name.
4. Keep a scope map: target path, owning/index root, project context, identity evidence, and freshness. Convert file hints relative to that target's index root. A `.csproj`/framework build scope is distinct from the repository's graph project context. Resolve each external target independently; a path outside the current checkout is useful context, not grounds for discarding it or automatically assigning a different project name.
5. Query the resolved context with `check_graph_freshness`, `resolve_exact_symbol` and the normal context tools. Use returned canonical node IDs; never fabricate IDs by prefixing the current project. If the target is unindexed, keep its source/config as explicitly unindexed evidence and continue useful scoped work.

Keep discovery narrow: inspect ancestors and root markers, not every sibling repository. Read external context relevant to the request; a reference path does not by itself authorize editing, indexing or running build scripts in that repository.

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
* investigating SQL scripts, schema changes or migration dependencies

If the task touches HTML, CSS, SCSS, selectors, style imports, or CSS variables, pair this skill with `codemeridian-frontend` so the context routing stays frontend-aware.

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
{"timestamp":"<ISO-8601 UTC time>","provider":"<codex|copilot|claude|continue|other>","project":"MyApp","kind":"<graph-call|codemeridian-tool|suggestion|tool-result|command|manual-fallback|test-run|stale-warning>","toolName":"<CodeMeridian MCP tool name when applicable>","command":"<shell command when applicable>","targetConfidence":"<exact|file-only|heuristic|stale, comma-separated if needed>","staleWarning":<true|false>,"contextPackStatus":"<full|degraded|failed when recording build_minimal_context results>","changeKind":"<direct-edit|extract|move|rename|split when lineage applies>","derivedFromFiles":["<repo-relative suggested source file>"],"derivedFromSymbols":["<source symbol when known>"],"plannedFolders":["<repo-relative target folder>"],"plannedNamespaces":["<target namespace>"],"files":["<repo-relative file path>"],"tests":["<repo-relative test file path>"]}
```

For each CodeMeridian tool call, record `kind=graph-call`, `toolName`, files suggested by the tool, tests suggested by the tool, `targetConfidence`, and `staleWarning` when present.

When recording a `build_minimal_context` result as a `tool-result` event, also record `contextPackStatus` as `full`, `degraded`, or `failed`.

When extracted or moved files are clearly derived from a suggested source, also record `changeKind` plus `derivedFromFiles` and optional `plannedFolders` or `plannedNamespaces`.

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
* `find_implementation_patterns` when the feature should mirror an existing entry/service/repository/test shape
* `resolve_exact_symbol` before editing a named class, method, interface, endpoint, or file
* `get_context_for_editing` when preparing a focused edit

For frontend work, expect these generic tools to surface indexed frontend edges such as `UsesClass`, `UsesId`, `DefinesSelector`, `ImportsStyle`, `UsesCssVariable`, and `DefinesCssVariable` before using frontend-only analysis.

Avoid loading large unrelated files unless CodeMeridian cannot answer the question.

### 4. Check Impact And Tests

Before behavior changes, refactors, deletions, or signature changes, inspect risk.

Prefer:

* `find_impact`
* `find_test_shield`
* `find_coverage_gaps`
* `find_unreferenced` before deleting code

### Cross-Project And Package Context

When a target is in another root, or producer/consumer code is relevant, call `find_cross_project_dependencies(projectContext)` for the involved indexed contexts and resolve useful endpoints in their own scopes. Use `find_connection` for exact endpoint relationships, then bounded edit context, impact and tests. Include a relevant external library as supporting context even when edits remain in the current repository.

For .NET package results, retain installed package/version, framework, producer source location and status. `verified_source` links can participate in normal source traversal. `associated_current_source` links support navigation and separate potential-consumer analysis; they do not prove that the checkout implements the installed package. `ambiguous`, `source_not_indexed` and `pending` do not prove absence of dependency or safety. Namespace similarity, directory proximity and matching package versions alone cannot establish a verified relationship. If package-aware results are unavailable on the connected server, state that limit and inspect only the relevant references/source; do not silently upgrade confidence.

### SQL Scripts And Database Dependencies

Use this workflow for `.sql` scripts, schema changes, migrations, views, foreign keys and questions about SQL readers/writers. SQL indexing is opt-in (`indexing.sql.enabled`) and currently bundles PostgreSQL only. It parses source offline with a native grammar compiled to WASM; it does not connect to or execute a database. `codemeridian index`, `--skip-sql`, `--dry-run`, `--list-capabilities` and `--no-incremental` control indexing/selection/reanalysis when indexing is part of the authorized task. Initial worker dependency restoration may need network access; no automatic database execution is implied.

Extend the target scope map with dialect, logical `databaseScope`, applicable `sources` rule and `searchPath` from the target's configuration. These are separate from graph `projectContext`. Resolve returned canonical IDs using schema-qualified names and file context; preserve quoted identifier case. Never merge objects across projects/database scopes because their names match, or infer a live database from a logical scope label.

| Need | Existing tool/workflow |
|---|---|
| Locate a SQL file, object, declaration or unresolved site | `query_codebase`, then `resolve_exact_symbol` with project/file hints |
| Check indexed evidence before edits | `check_graph_freshness` / `find_graph_drift`, plus SQL file status and source verification |
| Gather a bounded SQL edit neighborhood | `build_minimal_context`, `get_context_for_editing` |
| Inspect readers/writers, dependencies or schema-change impact | `find_impact`, `find_downstream` on exact IDs |
| Explain two objects/scripts or a join relationship | `find_connection`; a structural/join path is not runtime execution |
| Locate SQL configuration and its consumers | `find_config_definitions`, `find_config_usage`, then relevant local `meridian.json` |
| Locate implementation/docs/test seams | `analyze_feature_implementation_path`, `find_implementation_surface`, `search_documentation`, scoped test-shield/coverage queries |

These are generic graph tools, not a separate SQL-only MCP suite; do not invent dialect/database-scope tool parameters. SQL nodes include `DatabaseTable`, `DatabaseView`, `DatabaseColumn`, `DatabaseFunction`, neutral `DatabaseRelation`, and file-owned `SqlDeclaration` / `SqlReference`. Facts use `Contains`, `Declares`, `Reads`, `Writes`, `Alters`, `References`, `DependsOn` and `JoinsWith`. Follow a shared object's declaration/reference sites to its owning source files rather than assuming the object itself has one file location.

Unqualified dependency binding needs exactly one configured search-path schema. Missing/multiple schemas, conflicting declarations or unsupported constructs leave unresolved sites/partial coverage; a qualified `DatabaseRelation` need not have a known table/view catalog kind. Inspect per-file analysis status, unresolved reasons, parser provenance and diagnostics when returned; otherwise read the relevant source/config and worker output. Compiler/lint `find_diagnostics` is not a substitute for SQL parser status. Parse failures retain stale previous facts; normal impact/connection traversal excludes stale SQL edges, but other neighborhoods can still expose retained facts.

Function declarations do not imply analyzed function bodies. Dynamic SQL, migration execution order, runtime search paths, detailed column lineage and automatic reconciliation with legacy C#/TypeScript table IDs are not supported. Empty impact, method/class test shields or `find_unreferenced` results cannot prove a database object is unused or safe to change. Treat live-schema state, runtime consumers and unmatched cross-language table identities as explicit gaps.

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

### Optional Human Reasoning Handoff

When the user explicitly requests interactive code reasoning, or their goal is
learning rather than immediate execution, the resolved target and verified
source/test evidence may be handed to the `human-cognitive-seed` challenge
workflow. That skill owns challenge behavior.

Do not start a challenge for ordinary implementation work. Do not hand off a
fuzzy, stale, or unindexed target; index the real source and resolve the exact
canonical node first.

## Output Format

Start with this compact summary before implementation:

```text
Project scopes: target -> root -> projectContext (evidence / indexed status)
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

