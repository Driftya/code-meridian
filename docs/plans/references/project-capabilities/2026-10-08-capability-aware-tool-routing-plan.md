# Capability-Aware Tool And Workflow Routing

> Reference design. Current delivery status and task checkboxes: [Project capability completion checklist](../../2026-10-19-project-capability-completion-checklist.md). Original design dates and implementation notes are retained below.

Date: 2026-10-08

Status: substantially implemented for existing query families: shared requirements/policy, profile-aware per-step planning, executor and direct Application guards, result applicability, and transport/agent guidance. Worker evidence audits, narrowed composite requirements, root-scoped document queries, and future repository/PowerShell integrations remain open.

Depends on [project profiles](2026-10-08-project-discovery-capability-profile-plan.md). Part of the [roadmap](2026-10-08-project-capability-roadmap.md).

## Problem And Intended Behavior

### Implemented On 2026-10-12

Profile-backed Markdown/text edits use `document_review` without code-symbol resolution. Mixed repositories classify the target from inventory. Plans add versioned applicability/provenance; missing/stale discovery and unsupported callable edit targets block execution. Required document steps cannot be truncated into a successful shorter plan. Execution rechecks generation and readiness; direct document search uses the same policy. Search results exclude deleted/out-of-inventory sources and remain bounded. Profile discovery remains unrestricted.

This initial batch has been extended by the implementation below. Code/configuration/SQL coverage is still not inferred from process exit, file presence, or matching edge names.

### Implemented On 2026-10-16

`ContextWorkflowToolRequirements` and `ContextWorkflowToolPolicy` now declare and evaluate requirements for the central catalog. Planning, execution, 39 string-query entry points, five structured analysis families, document search, and keyword-related reads apply these requirements at the Application boundary. The original implementations remain behind explicit guarded entry points; transports share those public methods.

Plans retain per-step applicability and expose `omittedSteps` for unavailable optional analyses. Required unavailable steps block the route, and `maxSteps` cannot silently remove required steps. Explicit project identity and inventory target kinds control routing, including unfamiliar file extensions and canonical-node paths. Document edit recipes remain separate from callable resolution. Feature-document inputs retain their role as source evidence rather than becoming callable edit targets.

Execution rechecks current profile generation and readiness for every step, resolves exact inputs without guessing an ambiguous canonical target, and discards outputs when evidence changes. Configuration definition/usage steps are now executable. Direct string results include a factual applicability/coverage header; structured impact, connection, test-shield, cross-project, and context-pack results expose additive nullable `applicability`. `ok` means analysis executed, including legitimate empty findings; unavailable checks return their actual status without a clean-analysis message. Unverified test sections are omitted from otherwise usable context packs and recorded as degradations.

Project-wide relationship queries require full project outcomes. Only exact file-filtered symbol resolution accepts matching root evidence. Connection endpoints are checked independently; unverified cross-project paths are rejected. Embeddings require declared evidence and an available provider. GDS/query errors and cancellation cannot become successful empty results. Legacy explicit construction without the profile service retains its prior manual/advisory contract; production dependency injection supplies the profile service.

Core capabilities and outcome publication are unchanged by this routing batch: current CLI code/configuration/SQL outcomes remain unaudited and therefore unknown. Fixtures with explicitly verified capabilities demonstrate valid C#/TypeScript routes. Missing evidence does not activate a worker or install/ingest/re-index anything.

### Requirement Audit And Current Limits

| Family | Required evidence | Scope / limits |
|---|---|---|
| Exact symbol resolution | callable definitions | Exact file-filtered calls may use matching root outcomes |
| Structural queries, edit context, impact/downstream | callable definitions and dependencies | Full project evidence; observed non-callable targets do not inherit code support |
| Test shield / coverage gaps | definitions, dependencies, test-to-code | No test capability is invented from generic call edges |
| Configuration definitions / usage | configuration definitions; usage also requires code/config usage | Definition and consumer evidence are separate; current unaudited workers remain unknown |
| Namespace overview / cycles | namespace classification plus relevant definition/dependency evidence | No namespace support is inferred for a language merely from file presence |
| Architecture rules / history / reports | namespace/dependency facts plus explicit rules, history, or test evidence as used | Default-rule fallbacks do not establish configured rule readiness |
| GDS rankings / communities / bridges | callable graph plus audited graph projection | Mixed SQL/frontend nodes must not silently inherit callable projection coverage |
| Semantic analysis | declared embeddings and available provider, plus graph requirements | Provider failure is distinct from executed zero matches |
| Composite feature/edit/refactor reports | declared code, test, document, and/or projection evidence used by the report | Conservative union of requirements; optional report flags do not bypass the gate |
| Frontend connection / cascade | frontend relationships / dedicated cascade evidence | Source-order/specificity readiness is separate from generic frontend edges |
| Related keyword knowledge | keyword relationship evidence for the exact source project | No keyword analyzer outcomes are currently published; this remains unknown |
| SQL / PowerShell in generic callable tools | no audited callable compatibility | SQL targets are inapplicable; PowerShell analysis remains unsupported |
| Profile, freshness, dependency-impact inventory, client metadata | no semantic readiness prerequisite | Available for bounded discovery when profiles are unknown |

Across required analyzers, the policy evaluates current discovery and observed target applicability, analyzer support/configuration, declared capability support, then outcome scope/fingerprints/completion. Failures across analyzers are ordered failed, stale, partial, unsupported, disabled, not indexed, unknown. Reasons and limitations remain bounded. A full repository query cannot use an unrelated root's success.

Remaining work depends on stronger producer/storage evidence: audited per-worker/root capability publication, Node parser/runtime fingerprints, safe last-good evidence reuse across delta generations, root-filtered document/graph queries, narrower requirements for optional composite sections, and repository placement/Markdown reference/PowerShell capabilities from their own plans. These are explicit unknown/unsupported states, not completed integrations.

The original workflow recipes selected tools from goal/target signals without a persisted project capability profile. A Markdown target could enter a symbol-resolution workflow, and missing language evidence could look like a successful empty query.

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

### Verified On 2026-10-16

- Application suite: 511 passed, including applicability matrices, direct-call guards, project boundaries, workflow ordering, optional omissions, and generation-change rejection.
- Indexer suite: 364 passed for the existing discovery/outcome publication milestones.
- Focused MCP/API/schema/forwarding suite: 74 passed, including structured applicability and reviewed schema snapshots.
- No live Neo4j acceptance run was performed for this routing batch. Ready-language routes use explicit audited profile fixtures; these tests do not establish worker capability completeness.

## Rollout And Risks

Introduce additive contract fields or an explicit contract version; inventory downstream reports and clients before changing response shapes. Legacy profiles remain unknown, not automatically inapplicable. Keep readiness evaluation bounded and cache by profile generation where justified. Advertise test analysis only after its language-specific evidence is validated.
