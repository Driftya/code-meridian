# Add Context Workflow Planning

Status: implemented
Priority: P2
Feature: `plan_context_workflow`

## Summary

Add a CodeMeridian feature that plans how an agent should use the existing CodeMeridian tools for a task.

The tool should accept a goal, optional target, optional project, and optional workflow type. It then returns an ordered, safe, explainable workflow plan using the actual CodeMeridian tool surface.

The purpose is to help agents answer:

```text
Which CodeMeridian tools should I call, in what order, and why?
```

This turns CodeMeridian from a collection of graph tools into an agent navigation system. The feature should be especially useful for smaller models that may not know how to combine CodeMeridian tools correctly.

## Implemented

Implemented `plan_context_workflow` and the optional `execute_context_workflow` MCP tool.

The implementation uses a central Application catalog and deterministic recipes:

```text
src/Application/Services/ContextWorkflows/ContextWorkflowToolCatalog.cs
src/Application/Services/ContextWorkflows/ContextWorkflowPlanner.cs
```

`execute_context_workflow` is intentionally conservative. It executes read-only workflow steps available through `ICodebaseQueryService`, refuses graph-mutating plans unless explicitly approved, and stops on missing required inputs instead of hiding partial failure.

Detailed user documentation is in [Context workflows](../context-workflows.md).

## Problem

CodeMeridian has many useful tools across several categories:

* query and exploration
* keyword and documentation search
* configuration graph queries
* graph analytics
* architecture analysis
* semantic and hybrid search
* diagnostics
* implementation planning
* refactor planning
* graph freshness and stale knowledge
* ingestion and external concept linking
* client extension discovery
* multi-language route linking

An agent still has to decide which tools to call and in what order.

For example, before a refactor, a good agent might need:

```text
resolve_exact_symbol
check_graph_freshness
find_large_nodes
find_god_classes
suggest_extractions
find_impact
find_downstream
find_test_shield
find_diagnostics_for_node
build_minimal_context
```

Before implementing a feature, it might need:

```text
search_documentation
analyze_feature_implementation_path
find_implementation_surface
resolve_exact_symbol
check_graph_freshness
build_minimal_context
find_test_shield
find_diagnostics
```

Before an architecture review, it might need:

```text
get_architectural_overview
find_cycles
architecture_drift_history
find_architecture_violations
find_smell_paths
find_god_classes
find_high_churn
find_coverage_gaps
suggest_extractions
```

A strong model may infer this. A weaker model may not. Even a strong model wastes tokens rediscovering the same tool choreography.

## Goals

* Plan the correct sequence of CodeMeridian tools for a user goal.
* Use the actual current CodeMeridian feature/tool names.
* Support named workflow recipes for common agent tasks.
* Explain why each tool is included.
* Add stop conditions for stale graph data, unresolved targets, and missing evidence.
* Prefer exact graph facts before heuristic expansion.
* Prefer safe read-only graph/documentation/diagnostic analysis.
* Make CodeMeridian easier to use from Copilot, Codex, Claude Code, Continue, Cline, and local agents.
* Keep planning deterministic and testable.
* Keep workflow logic in Application, not MCP handlers.

## Non-goals

* Do not edit source files.
* Do not run arbitrary shell commands.
* Do not run tests in the first slice.
* Do not mutate graph state unless the workflow explicitly chooses an ingestion or rebuild workflow.
* Do not hide the individual tools being recommended.
* Do not replace existing CodeMeridian tools.
* Do not depend on an LLM API inside CodeMeridian.
* Do not invent tools that are not registered or documented.

## Proposed Tool

### `plan_context_workflow`

Plans the recommended CodeMeridian tool sequence for a task.

Example:

```text
Plan how to use CodeMeridian before refactoring CodebaseQueryService.
```

Example:

```text
Plan how to use CodeMeridian to implement docs/features/34-add-context-workflow-planning.md.
```

Example:

```text
Plan how to use CodeMeridian for an architecture review.
```

## Optional Future Tool

### `execute_context_workflow`

Runs an approved read-only workflow plan and returns a combined result.

This should be a later slice after `plan_context_workflow` is stable.

The first implementation should plan only.

## Actual Tool Surface To Plan Against

The planner should understand the current CodeMeridian tools grouped by intent.

### Query and Exploration

```text
query_codebase
get_architectural_overview
search_documentation
rebuild_keyword_graph
classify_keywords
find_related_knowledge
```

### Configuration Graph

```text
find_config_definitions
find_config_usage
```

### Graph Analytics

```text
find_impact
find_hotspots
find_connection
find_unreferenced
find_cross_project_dependencies
find_coverage_gaps
find_test_shield
find_recently_changed
find_large_nodes
get_context_for_editing
build_minimal_context
find_god_classes
find_downstream
find_cycles
architecture_drift_history
find_architecture_violations
find_smell_paths
find_high_churn
```

### Semantic and Hybrid Search

```text
find_similar_nodes
hybrid_search
find_duplicate_candidates
```

### Diagnostics

```text
find_diagnostics
find_diagnostics_for_node
```

### Implementation and Refactor Planning

```text
find_implementation_surface
analyze_feature_implementation_path
replace_surface
suggest_extractions
resolve_exact_symbol
```

### Freshness and Knowledge Health

```text
check_graph_freshness
find_graph_drift
find_stale_knowledge
knowledge_decay
```

### Ingestion and External Concepts

```text
ingest_code_node
ingest_relationship
ingest_document
link_external_concept
clear_project_knowledge
clear_code_graph
```

### Client Extensions

```text
get_client_extension_contract
list_client_extension_examples
get_client_extension_example
```

## Workflow Types

The planner should support these named workflow types.

### `before_edit`

Use when the user wants to change a known class, method, file, endpoint, or feature.

Recommended tool sequence:

```text
resolve_exact_symbol
check_graph_freshness
get_context_for_editing
find_impact
find_downstream
find_test_shield
find_diagnostics_for_node
build_minimal_context
```

Rules:

* Start with `resolve_exact_symbol` when a target is provided.
* Use `check_graph_freshness` before trusting exact graph paths.
* Use `get_context_for_editing` before broader impact tools.
* Use `build_minimal_context` near the end so the context pack includes risk and test signals.

### `feature_implementation`

Use when the user wants to implement a feature from a docs path, issue title, or goal statement.

Recommended tool sequence:

```text
search_documentation
analyze_feature_implementation_path
find_implementation_surface
resolve_exact_symbol
check_graph_freshness
find_related_knowledge
build_minimal_context
find_test_shield
find_diagnostics
```

Rules:

* If the input contains `docs/features/*.md`, start with `analyze_feature_implementation_path`.
* If the target is vague, use `find_implementation_surface` before `resolve_exact_symbol`.
* Use `search_documentation` when the goal mentions docs, ADRs, rules, or feature specs.
* Use `find_related_knowledge` when direct graph links are weak.

### `refactor_planning`

Use when the user wants to split, move, rename, extract, reduce, or reorganize code.

Recommended tool sequence:

```text
resolve_exact_symbol
check_graph_freshness
find_large_nodes
find_god_classes
suggest_extractions
find_impact
find_downstream
find_test_shield
find_diagnostics_for_node
build_minimal_context
```

Rules:

* Use `find_large_nodes` for size evidence.
* Use `find_god_classes` when the target may be large and heavily depended on.
* Use `suggest_extractions` for tightly connected extraction candidates.
* Use `find_impact` and `find_downstream` before recommending service boundary changes.
* Use `find_test_shield` before recommending migration steps.

### `responsibility_slice_planning`

Use when the user wants folder, namespace, and responsibility extraction guidance for a large class or service.

Recommended tool sequence:

```text
resolve_exact_symbol
check_graph_freshness
find_large_nodes
find_god_classes
suggest_extractions
find_hotspots
find_test_shield
find_coverage_gaps
find_diagnostics_for_node
build_minimal_context
```

Recommended responsibility-slice tool:

```text
suggest_responsibility_slices
```

Rules:

* Use `suggest_responsibility_slices` as the primary planning substrate, then compare with `suggest_extractions`, `find_large_nodes`, `find_god_classes`, `find_hotspots`, and `find_test_shield`.
* Recommend namespace/folder suggestions only as heuristic output unless explicit project conventions are indexed.
* Label responsibility grouping as heuristic unless supported by strong graph evidence.

### `architecture_review`

Use when the user asks about boundaries, layers, cycles, dependency smells, erosion, or project shape.

Recommended tool sequence:

```text
get_architectural_overview
find_cycles
architecture_drift_history
find_architecture_violations
find_smell_paths
find_god_classes
find_hotspots
find_high_churn
find_coverage_gaps
suggest_extractions
```

Rules:

* Start broad with `get_architectural_overview`.
* Use `find_cycles` before deeper smell paths.
* Use `architecture_drift_history` to show erosion over time.
* Use `find_architecture_violations` and `find_smell_paths` together.
* Use `find_high_churn` and `find_hotspots` to prioritize risk.

### `dependency_replacement`

Use when the user wants to replace a package, API, abstraction, framework, or library.

Recommended tool sequence:

```text
replace_surface
find_impact
find_downstream
find_test_shield
find_diagnostics
build_minimal_context
```

Optional additions:

```text
find_config_usage
find_config_definitions
search_documentation
```

Rules:

* Start with `replace_surface`.
* Add config tools when the dependency is configured through options, environment variables, appsettings, Docker Compose, or `.env`.
* Use `find_impact` for callers.
* Use `find_downstream` for dependencies the old surface touches.
* Use `find_test_shield` before recommending replacement order.

### `knowledge_health`

Use when the user asks about stale docs, graph drift, old knowledge, weak links, or outdated indexed facts.

Recommended tool sequence:

```text
find_graph_drift
check_graph_freshness
find_stale_knowledge
knowledge_decay
find_related_knowledge
search_documentation
```

Rules:

* Use `find_graph_drift` when the question is about whether the graph is reliable.
* Use `find_stale_knowledge` or `knowledge_decay` for stale docs and orphaned knowledge.
* Use `find_related_knowledge` when the goal is to connect docs, diagnostics, and code through the keyword graph.

### `diagnostic_review`

Use when the user asks about build errors, analyzer warnings, TypeScript errors, lint issues, or diagnostics near a target.

Recommended tool sequence:

```text
find_diagnostics
resolve_exact_symbol
find_diagnostics_for_node
find_impact
find_test_shield
build_minimal_context
```

Rules:

* Use `find_diagnostics` first when no exact target is known.
* Use `find_diagnostics_for_node` after resolving a target.
* Add `find_impact` if fixing the diagnostic may affect callers.
* Add `build_minimal_context` before editing.

### `configuration_review`

Use when the user asks about config keys, environment variables, options binding, or appsettings.

Recommended tool sequence:

```text
find_config_definitions
find_config_usage
find_related_knowledge
find_impact
find_diagnostics
build_minimal_context
```

Rules:

* Start with `find_config_definitions` when a key is known.
* Use `find_config_usage` to find code that reads or binds the key.
* Use `find_related_knowledge` to connect docs and diagnostics.
* Never include secret-like values in logs or summaries.

### `cross_project_trace`

Use when the user asks how projects, frontend/backend, APIs, or modules connect.

Recommended tool sequence:

```text
find_cross_project_dependencies
find_connection
find_downstream
find_impact
build_minimal_context
```

Optional additions:

```text
search_documentation
find_related_knowledge
```

Rules:

* Use `find_cross_project_dependencies` first for project boundaries.
* Use `find_connection` for path questions between two known nodes.
* Use `build_minimal_context` when the result will guide source inspection.

### `semantic_discovery`

Use when the user asks for similar code, duplicate patterns, or examples near a subsystem.

Recommended tool sequence:

```text
find_similar_nodes
hybrid_search
find_duplicate_candidates
find_related_knowledge
find_test_shield
build_minimal_context
```

Rules:

* Use `find_similar_nodes` for broad semantic similarity.
* Use `hybrid_search` when the user mentions a subsystem or nearby reference node.
* Use `find_duplicate_candidates` when the intent is refactoring duplicate code.
* Label semantic results as embedding-dependent.

### `documentation_ingestion`

Use when the user wants CodeMeridian to remember project knowledge, docs, external concepts, routes, tables, message topics, or service dependencies.

Recommended tool sequence:

```text
ingest_document
link_external_concept
ingest_relationship
find_related_knowledge
find_stale_knowledge
```

Rules:

* Do not use ingestion tools in normal planning workflows unless the user explicitly wants to add graph knowledge.
* Treat ingestion as graph mutation.
* Require explicit confirmation for destructive tools such as `clear_project_knowledge` and `clear_code_graph`.

### `client_extension_discovery`

Use when the user wants to discover the GraphQL-backed client extension contract or load curated example queries for client-owned behavior.

Recommended tool sequence:

```text
get_client_extension_contract
list_client_extension_examples
```

Then load a concrete example when needed:

```text
get_client_extension_example
```

Rules:

* Client behavior stays client-owned; CodeMeridian should expose facts, schema, auth, and bounded graph access only.
* `get_client_extension_example` should be used after the client has selected a relevant example id.

## Tool Contract

### `plan_context_workflow` input

```json
{
  "goal": "Plan how to implement docs/features/34-add-context-workflow-planning.md.",
  "target": "docs/features/34-add-context-workflow-planning.md",
  "project": "CodeMeridian",
  "workflowType": "feature_implementation",
  "maxSteps": 8,
  "includeStopConditions": true,
  "includeExecutionHints": true
}
```

### `plan_context_workflow` output

```json
{
  "workflowId": "feature_implementation",
  "workflowType": "feature_implementation",
  "project": "CodeMeridian",
  "target": "docs/features/34-add-context-workflow-planning.md",
  "summary": "Plan graph-backed feature implementation discovery before editing source.",
  "requiresApprovalBeforeExecution": false,
  "estimatedCost": "medium",
  "steps": [
    {
      "order": 1,
      "tool": "analyze_feature_implementation_path",
      "required": true,
      "purpose": "Map the feature document to likely implementation surfaces, related tests, docs, risk, and missing graph evidence.",
      "inputHints": {
        "featurePath": "docs/features/34-add-context-workflow-planning.md"
      },
      "expectedOutput": "Implementation path analysis with confidence and risk.",
      "stopCondition": "Stop if the feature file cannot be found and no fallback goal text was provided."
    },
    {
      "order": 2,
      "tool": "find_implementation_surface",
      "required": true,
      "purpose": "Rank likely files, classes, and methods to edit for the feature goal.",
      "expectedOutput": "Candidate edit surfaces with exact, file-only, heuristic, or stale confidence."
    },
    {
      "order": 3,
      "tool": "resolve_exact_symbol",
      "required": false,
      "purpose": "Resolve the highest-confidence implementation surface to canonical node IDs.",
      "expectedOutput": "Exact or file-only node resolution."
    },
    {
      "order": 4,
      "tool": "check_graph_freshness",
      "required": true,
      "purpose": "Check whether indexed file paths, line metadata, and timestamps are reliable.",
      "expectedOutput": "Freshness confidence and re-index recommendation if needed."
    },
    {
      "order": 5,
      "tool": "find_related_knowledge",
      "required": false,
      "purpose": "Find related docs, diagnostics, endpoints, and symbols through the keyword graph.",
      "expectedOutput": "Related knowledge with lexical confidence."
    },
    {
      "order": 6,
      "tool": "find_test_shield",
      "required": true,
      "purpose": "Find tests that protect the likely implementation surface.",
      "expectedOutput": "Direct shield, indirect shield, and unshielded path nodes."
    },
    {
      "order": 7,
      "tool": "find_diagnostics",
      "required": false,
      "purpose": "Check existing diagnostics before planning edits.",
      "expectedOutput": "Compiler, analyzer, TypeScript, or lint diagnostics."
    },
    {
      "order": 8,
      "tool": "build_minimal_context",
      "required": true,
      "purpose": "Build a bounded context pack for source inspection and implementation.",
      "expectedOutput": "Likely files, graph paths, tests, diagnostics, token estimate, and model guidance."
    }
  ],
  "finalResponseGuidance": [
    "State graph freshness before giving implementation advice.",
    "Separate exact graph facts from heuristic matches.",
    "List files to inspect before editing.",
    "List tests to run or add.",
    "Do not claim source was edited."
  ]
}
```

## Planning Rules

The planner should be deterministic and recipe-driven in the first slice.

Rules:

* Use actual registered CodeMeridian tool names only.
* Prefer `resolve_exact_symbol` before exact node workflows.
* Prefer `check_graph_freshness` before trusting exact implementation targets.
* Prefer `find_graph_drift` for broad graph trust questions.
* Prefer `get_context_for_editing` before `find_impact` for local edit context.
* Prefer `build_minimal_context` near the end of workflows.
* Prefer `find_test_shield` before refactor, replacement, or implementation recommendations.
* Prefer `search_documentation` when the goal mentions docs, ADRs, README, feature specs, or project rules.
* Prefer `find_related_knowledge` when structural graph links may be missing.
* Prefer `analyze_feature_implementation_path` when the input contains `docs/features/*.md`.
* Prefer `find_implementation_surface` when the target is a feature goal rather than an exact symbol.
* Prefer `replace_surface` when the goal mentions replacing, migrating, swapping, or removing a dependency.
* Prefer `find_config_definitions` and `find_config_usage` for appsettings, env vars, options, Docker Compose env, or `meridian.json`.
* Prefer `architecture_drift_history`, `find_architecture_violations`, and `find_smell_paths` for architecture erosion and boundary questions.
* Prefer `find_similar_nodes`, `hybrid_search`, and `find_duplicate_candidates` for semantic similarity and duplicate-code refactor questions.
* Never include destructive tools like `clear_project_knowledge` or `clear_code_graph` unless the workflow type is explicitly administrative and user intent is explicit.

## Stop Conditions

Each workflow step may define a stop condition.

Common stop conditions:

* Target cannot be resolved.
* Graph freshness is stale for exact implementation work.
* Feature document cannot be found.
* Required graph metadata is missing.
* Requested tool requires embeddings but embeddings are not indexed.
* Requested workflow would require destructive graph mutation without explicit user approval.
* Search returns only heuristic evidence and no source verification path.

## Architecture

### Application

Owns workflow planning.

Suggested types:

```text
IContextWorkflowPlanner
ContextWorkflowPlanner
ContextWorkflowPlanRequest
ContextWorkflowPlanResult
ContextWorkflowStep
ContextWorkflowRecipe
ContextWorkflowType
ContextWorkflowStopCondition
ContextWorkflowToolCatalog
```

The planner should use a catalog of known CodeMeridian tools and recipes. Tool names should not be scattered as magic strings.

### Application Tool Catalog

Create a typed catalog that represents current CodeMeridian tools.

Example categories:

```text
QueryAndExploration
ConfigurationGraph
GraphAnalytics
SemanticSearch
Diagnostics
ImplementationPlanning
FreshnessAndKnowledge
Ingestion
ClientExtensions
```

Each catalog entry should include:

```text
Tool name
Category
Description
Whether it is read-only
Whether it mutates graph data
Whether it is destructive
Whether it requires embeddings
Whether it requires an exact target
Whether it can work from a vague goal
Typical inputs
Typical outputs
```

### Infrastructure

No workflow decision logic should live in Infrastructure.

Infrastructure continues to implement graph, documentation, diagnostics, and persistence ports.

### MCP Server / Presentation

Expose a thin MCP tool:

```text
plan_context_workflow
```

The MCP handler should:

* validate request shape
* call Application
* return the plan
* log boundary failures once
* avoid embedding recipes or tool-ordering rules

## Future Execution Slice

A later feature can add:

```text
execute_context_workflow
```

This tool may run approved read-only workflows and return a combined summary.

Execution must not:

* edit source files
* run arbitrary shell commands
* call destructive graph tools without explicit approval
* hide failed required steps

Execution may:

* call read-only CodeMeridian analysis services
* summarize each step
* return recommended files to inspect
* return recommended tests to run or add
* return warnings and stop reasons

## Logging And Observability

Use structured logging with `ILogger<T>` only.

Information logs:

```text
Planned context workflow {WorkflowType} for {Target} in project {ProjectName} with {StepCount} steps.
```

Trace logs:

```text
Workflow recipe {WorkflowType} selected because goal matched {MatchedSignals}.
```

Rules:

* Use Trace for per-rule matching details.
* Guard hot-path Trace logs with `IsEnabled(LogLevel.Trace)`.
* Do not log source snippets.
* Do not log secrets, tokens, credentials, config values, or personal data.
* Log exceptions once at the MCP/Application boundary.

## Error Handling

* Invalid workflow type returns a validation error with supported workflow types.
* Missing target returns a discovery-first plan when possible.
* Unknown tool names in recipes should fail tests, not runtime.
* Tool catalog and docs should stay synchronized.
* Stale graph returns warnings and recommends re-indexing.
* Embedding-dependent workflows should warn when embeddings are unavailable.
* Preserve stack traces with `throw;` when rethrowing.

## Testing Requirements

### Unit Tests

Add tests for:

* selecting `before_edit` from edit/change wording
* selecting `feature_implementation` from `docs/features/*.md`
* selecting `refactor_planning` from split/extract/refactor wording
* selecting `responsibility_slice_planning` from folder/namespace/slice wording
* selecting `architecture_review` from boundary/cycle/smell/erosion wording
* selecting `dependency_replacement` from replace/migrate/swap wording
* selecting `knowledge_health` from stale/docs/knowledge wording
* selecting `diagnostic_review` from diagnostic/error/analyzer wording
* selecting `configuration_review` from config/env/options wording
* selecting `semantic_discovery` from similar/duplicate/pattern wording
* ordering exact symbol resolution before exact graph traversal
* ordering freshness checks before implementation recommendations
* placing `build_minimal_context` near the end
* excluding destructive tools from normal workflows
* respecting `maxSteps`
* omitting optional steps when requested
* no recipe references a missing tool

### MCP Contract Tests

Add tests for:

* `plan_context_workflow` returns stable shape
* invalid workflow type returns clear error
* missing target still returns a discovery-first plan
* feature file path returns feature implementation recipe
* architecture review returns architecture tool sequence
* dependency replacement returns replacement workflow sequence

### Documentation Tests

Add a test or validation helper that compares the tool catalog with documented feature names so recipes do not drift from `docs/features.md`.

## Acceptance Criteria

* `plan_context_workflow` is available as an MCP tool.
* The planner uses the actual current CodeMeridian tool names.
* The planner supports at least:

  * `before_edit`
  * `feature_implementation`
  * `refactor_planning`
  * `responsibility_slice_planning`
  * `architecture_review`
  * `dependency_replacement`
  * `knowledge_health`
  * `diagnostic_review`
  * `configuration_review`
  * `cross_project_trace`
  * `semantic_discovery`
  * `documentation_ingestion`
  * `client_extension_discovery`
* The plan includes ordered steps, purpose, required/optional flag, expected output, and stop conditions.
* The plan explains why each tool is included.
* The plan avoids source edits and destructive graph actions.
* Workflow recipes are deterministic and covered by tests.
* Recipe tool names are validated against a central tool catalog.
* Planning logic lives in Application.
* MCP handler remains thin.
* Documentation is added to `docs/features.md`.
* A todo entry links to this feature file.

