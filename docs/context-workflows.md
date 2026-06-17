# Context Workflows

CodeMeridian can plan and optionally execute deterministic tool workflows for agent tasks. This helps an agent decide which CodeMeridian tools to call, in what order, and where to stop when graph evidence is weak.

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
  "includeOptionalSteps": true,
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
  "maxSteps": 1,
  "includeOptionalSteps": false
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
| `extension_agent_routing` | Listing or calling registered project agents |

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
