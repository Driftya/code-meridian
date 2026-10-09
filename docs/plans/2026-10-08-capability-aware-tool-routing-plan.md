# Capability-Aware Tool And Workflow Routing

Date: 2026-10-08

Status: planned.

Depends on [project profiles](2026-10-08-project-discovery-capability-profile-plan.md). Part of the [roadmap](2026-10-08-project-capability-roadmap.md).

## Problem And Intended Behavior

Current workflow recipes select tools from goal/target signals without a persisted project capability profile. A Markdown target can enter a symbol-resolution workflow, and missing language evidence can look like a successful empty query.

Use project readiness, target kind, and task intent to select tools. Apply the same applicability policy to planned workflows, execution, and direct public calls. Keep tool descriptions static; runtime applicability is project-specific.

## Scope

- Reuse the existing deterministic planner and central catalog.
- Add capability requirements and supported target categories to catalog descriptors.
- Keep generic graph tools shared across languages when their contracts support the relevant nodes and relationships.
- Add document-oriented edit/review recipes without resolving Markdown headings as code symbols.
- Scope decisions to the target/root. Repository-level C# presence does not make a manuscript target a code target.
- Do not replace the workflow engine, hide tools dynamically per session, or require an LLM to judge applicability.

## Applicability Contract

Separate applicability, readiness, and actual execution results internally. Suggested externally visible statuses:

| Status | Meaning |
|---|---|
| ready | Required target and evidence are available; the analysis may run |
| not_applicable | Tool semantics do not apply to this known target or scope |
| not_indexed | Relevant files are observed but required facts have not been published |
| unsupported | The required analyzer capability is not implemented |
| disabled | Analyzer support exists but configuration excludes it |
| partial / stale | Evidence exists with an explicit completeness/freshness limitation |
| unknown | Profile or scope discovery is absent or incomplete |
| failed | Analysis or evidence acquisition failed |
| ok | Analysis executed; an empty finding set is a valid result only with stated coverage |

Define deterministic precedence when several conditions apply and retain bounded reasons rather than collapsing them into one ambiguous string. Syntax warnings and missing profiles are not architecture success.

## Tool Requirements

Catalog requirements can express bounded alternatives such as callable definitions plus supported dependency evidence. No arbitrary expression language is needed.

| Tool family | Requirements |
|---|---|
| Code symbol resolution | Supported code-symbol inventory for the target |
| Impact / connection | Relationship families and node kinds supported by that exact tool |
| Test shield | Supported test-to-code relationships and their completeness |
| Documentation search | Successfully indexed document text |
| Config definitions | Indexed configuration entries |
| Config usage | Supported code/config usage evidence |
| Namespace architecture | Namespace-classified code and configured code-dependency rules |
| Repository placement | Completed inventory and validated placement rules, after constraint implementation |
| Markdown references | Parsed reference evidence, after constraint implementation |
| Frontend cascade | Indexed selectors/declarations with supported specificity/source-order evidence |

Do not enable a tool merely because another analyzer emits an edge with the same name. Audit repository queries, filters, identifiers, snippets, and response types before advertising SQL or PowerShell compatibility.

## Planning And Execution Flow

1. Resolve the explicit project and fetch a bounded profile.
2. Classify an explicit target from inventory, not only filename heuristics.
3. Choose the task recipe and evaluate requirements for each step.
4. Replace inapplicable code-oriented steps with an existing supported document recipe where appropriate.
5. Explain omitted optional steps. A missing required capability blocks that route; it must not silently produce a successful shortened plan.
6. Attach profile generation and evaluated scope to the plan.
7. Revalidate readiness at execution time; a changed profile or missing target cannot be bypassed by a previously valid plan.
8. Guard direct tool/API calls at the shared Application boundary.
9. Return results with applicability, coverage, limitations, and next actions.

Profile discovery must be exempt from its own readiness gate. Never automatically re-index, install dependencies, execute scripts, or ingest documents to make a read-only workflow ready.

For unknown profiles, preserve explicit advisory/manual discovery behavior. Do not assume the repository is code-only or document-only. Avoid choosing from another project's capabilities when project identity is ambiguous.

## Existing Surfaces And Planned Changes

- `src/Application/Services/ContextWorkflows/ContextWorkflowToolCatalog.cs`: descriptor requirements and target kinds.
- `ContextWorkflowModels.cs`: additive/versioned applicability and plan provenance fields.
- `ContextWorkflowPlanner.cs`: deterministic profile-aware recipes.
- `src/Application/Services/CodebaseQueryService.ContextWorkflows.cs`: profile resolution, executor revalidation, status propagation.
- Other Application query methods: shared applicability checks for direct invocation, introduced in small tool-family batches.
- `src/McpServer/Tools/CodebaseTools.ContextWorkflows.cs`, SDK/API contracts: forwarding and schema/discovery coverage.
- Repository agent guidance and bundled capability packs: conditional code/document/configuration workflows.

Core contracts stay framework-independent. Applicability decisions belong in Application; query storage facts belong in Infrastructure. Keep Cypher out of the planner.

## Phases

1. Audit tools and declare accurate requirements for existing C#, TypeScript, document, configuration, and SQL capabilities.
2. Add profile-aware planning plus a Markdown edit/review recipe.
3. Add executor and direct-call guards with compatible response handling.
4. Integrate repository constraints and PowerShell as those plans publish supported capabilities.
5. Update agent guidance, tool descriptions, feature docs, and dependency-impact inventory.

## Verification And Acceptance

- Extend `ContextWorkflowPlannerTests` and `CodebaseQueryServiceContextWorkflowTests`.
- Markdown-only edit routes never require code-symbol resolution or namespace architecture.
- A mixed repository chapter target uses document capabilities; a C# target uses its code scope.
- PowerShell detected but not supported returns unsupported; enabled but unpublished facts return not indexed.
- Unknown/incomplete discovery does not establish absence.
- Missing required steps block execution; omitted optional steps remain visible.
- Revalidate plans after deletion, capability changes, or failed indexing.
- Direct calls and workflow execution agree on statuses.
- Existing valid C#/TypeScript workflows preserve ordering, focused defaults, mutation guards, and cancellation.
- Test API/MCP forwarding and schema compatibility; callers that consume markdown still receive a concise explanation.
- An executed check with zero findings is distinguishable from a check that did not run.

Use isolated profile fixtures; no dependency on `ava-sporelight` for unit tests.

## Rollout And Risks

Introduce additive contract fields or an explicit contract version; inventory downstream reports and clients before changing response shapes. Legacy profiles remain unknown, not automatically inapplicable. Keep readiness evaluation bounded and cache by profile generation where justified. Advertise test analysis only after its language-specific evidence is validated.
