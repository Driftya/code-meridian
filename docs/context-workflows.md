# Context Workflows

CodeMeridian can plan and optionally execute deterministic tool workflows for agent tasks. This helps an agent decide which CodeMeridian tools to call, in what order, and where to stop when graph evidence is weak.

## Project-Aware Document Routing

Supply `projectContext` so the planner can read the uploaded project profile. An observed Markdown/text edit target selects `document_review`: `get_project_profile`, then `search_documentation`. A Markdown-only repository can use the same route without a target. Mixed repositories classify the target from inventory; a chapter does not inherit an application's callable capabilities. Unsupported script edit targets are blocked.

Plans expose additive contract version `1.1` and `applicability` with status, profile generation, observed target kind, scope, and reasons. Missing or stale profiles keep manual advice but block callable edit execution. Document readiness distinguishes missing/unknown evidence, disabled/unsupported analyzers, unpublished indexing, partial/failed passes, and successful complete project document coverage. Required document steps are retained and the route is blocked if `maxSteps` would omit one.

Execution revalidates requirements and profile generation for every step and discards stale outputs. Direct analysis calls use the same Application policy. Profile lookup, freshness inspection, client metadata, and the static tool-dependency inventory remain available without semantic readiness. Neither planning nor execution re-indexes, ingests documents, installs workers, or executes repository scripts to become ready.

The catalog now covers callable, configuration, test, architecture, GDS, semantic, frontend, and keyword query requirements. Required unavailable steps block execution; optional unavailable steps appear in `omittedSteps` with reasons. `maxSteps` can omit optional steps but cannot create a successful shortened route by removing a required step. Verified C#/TypeScript fixtures preserve normal ordering and focused defaults.

Applicability includes version, project, generation, target kind, scope, coverage, bounded reasons/limitations, and next actions. Plans and executions use additive contract version `1.1`. String analyses return `Tool applicability status: ...`; structured impact, connection, test-shield, cross-project, and minimal-context results add `applicability`. `ok` identifies an executed check, including zero findings. Unknown, disabled, unsupported, not-indexed, partial, stale, and failed checks do not masquerade as clean analyses. Context packs can omit unaudited test sections and record the omission as a degradation.

Relationship queries require complete project evidence. Exact file-filtered symbol lookup alone can use matching root coverage. Canonical targets are checked against their owning project and accepted inventory; unverified cross-project connections are rejected. Composite reports conservatively require all declared evidence families even when optional flags narrow a section. Current CLI code/configuration/SQL coverage is still unaudited, and keyword/cascade/history/projection capabilities are not invented. Root-scoped document queries, safe delta evidence reuse, repository placement/reference checks, and PowerShell analysis remain planned. See the [requirement audit](plans/references/project-capabilities/2026-10-08-capability-aware-tool-routing-plan.md) and [completion checklist](plans/2026-10-19-project-capability-completion-checklist.md) for exact limits.

## Tools

### `plan_context_workflow`

Returns a JSON workflow plan. The plan includes:

- selected workflow type
- ordered tool steps
- required versus optional flags
- purpose for each step
- input hints
- expected output
- stop conditions
- execution hints
- warnings for mutation, stale graph risk, missing targets, and embedding-dependent tools

If `includeOptionalSteps` is omitted, the planner now uses workflow-aware defaults: narrow workflows such as `before_edit`, `diagnostic_review`, `configuration_review`, and `dependency_replacement` return required steps only, while broader workflows still include their optional orientation steps. Set `includeOptionalSteps` to `true` for the broader recipe or `false` for required-only steps regardless of workflow type.

Example:

```text
Plan how to use CodeMeridian before refactoring CodebaseQueryService.
```

Example input:

```json
{
  "goal": "Implement docs/features/43-add-context-workflow-planning.md.",
  "target": "docs/features/43-add-context-workflow-planning.md",
  "projectContext": "CodeMeridian",
  "workflowType": "feature_implementation",
  "maxSteps": 8,
  "includeStopConditions": true,
  "includeExecutionHints": true
}
```

### `execute_context_workflow`

Runs an approved workflow plan and returns JSON step results. This tool is conservative:

- it executes only read-only CodeMeridian query tools in this slice
- it refuses graph-mutating or destructive workflows unless `allowGraphMutation` is true
- it still does not execute unsupported mutating tools directly
- it stops when a required step lacks a target, second target, replacement target, or other required input
- it preserves each step status instead of hiding partial failure

Use `plan_context_workflow` first when the user only asked for a plan.

Example:

```json
{
  "goal": "Review diagnostics before editing.",
  "projectContext": "CodeMeridian",
  "workflowType": "diagnostic_review",
  "maxSteps": 1
}
```

## Workflow Types

The planner supports these named workflow types:

| Workflow | Use when |
|---|---|
| `before_edit` | Changing a known class, method, file, endpoint, or feature |
| `feature_implementation` | Implementing a feature from a docs path, issue title, or goal |
| `refactor_planning` | Splitting, moving, renaming, extracting, or reorganizing code |
| `responsibility_slice_planning` | Planning folder, namespace, and responsibility extraction for a large class |
| `architecture_review` | Reviewing boundaries, layers, cycles, dependency smells, or project shape |
| `dependency_replacement` | Replacing a package, API, abstraction, framework, or library |
| `knowledge_health` | Checking stale docs, graph drift, old knowledge, weak links, or outdated indexed facts |
| `diagnostic_review` | Reviewing compiler, analyzer, TypeScript, lint, or build diagnostics |
| `configuration_review` | Reviewing config keys, env vars, options binding, appsettings, or Docker Compose env |
| `cross_project_trace` | Tracing projects, frontend/backend paths, APIs, routes, or modules |
| `semantic_discovery` | Finding similar code, duplicate patterns, or examples |
| `documentation_ingestion` | Explicitly adding docs, external concepts, routes, tables, topics, or relationships to the graph |
| `client_extension_discovery` | Discovering the GraphQL-backed client extension contract and curated examples |

If `workflowType` is omitted, CodeMeridian infers it from the goal and target. A `docs/features/*.md` target selects `feature_implementation`.

## Safety Rules

The planner is recipe-driven and deterministic:

- exact-target workflows start with `resolve_exact_symbol`
- graph trust checks use `check_graph_freshness` or `find_graph_drift`
- edit workflows place `build_minimal_context` near the end after risk and test signals
- refactor and replacement workflows include `find_test_shield`
- feature workflows use `analyze_feature_implementation_path` for feature docs
- config workflows use `find_config_definitions` and `find_config_usage`
- architecture workflows pair `find_architecture_violations` with `find_smell_paths`
- semantic workflows label embedding-dependent tools
- ingestion and admin workflows are marked as graph-mutating
- destructive tools such as `clear_project_knowledge` and `clear_code_graph` are never included in normal workflows

## Adding Tools Or Recipes

Workflow planning uses a central application catalog:

```text
src/Application/Services/ContextWorkflows/ContextWorkflowToolCatalog.cs
```

Add new CodeMeridian tool names there first, including category and safety flags. Then add or update recipes in:

```text
src/Application/Services/ContextWorkflows/ContextWorkflowPlanner.cs
```

Tests validate that every recipe tool exists in the catalog and that the public docs mention both workflow tools.
