# MCP 2 Capability Adoption Plan

- Status: local implementation substantially complete; live deployment, graph-data acceptance, and host compatibility remain intentionally deferred
- Date: 2026-08-04
- Scope: adopt the MCP 2026-07-28 / C# SDK 2.x capabilities that materially improve CodeMeridian's safety, machine-readability, long-running operations, client experience, and operability
- Primary host: `src/McpServer`
- Package baseline: `ModelContextProtocol.AspNetCore` 2.x
- Planning principle: ship small independently useful slices; do not enable every new protocol feature merely because the SDK supports it

## Implementation Status (2026-08-04)

Completed in the current working tree:

- [x] Upgrade and run the stable 2.0 SDK over explicit stateless Streamable HTTP.
- [x] Give all 62 tools reviewed titles and behavior annotations, enforced through the real in-memory discovery endpoint.
- [x] Preserve Markdown while adding typed, schema-validated structured results to three client-extension tools and `find_connection`.
- [x] Evaluate and remove the optional project-context header pilot after real endpoint tests exposed the SDK's required integrity-mirror behavior.
- [x] Add private five-minute `tools/list` caching hints.
- [x] Add bounded logs, activities, counters, and duration metrics without arguments or credentials.
- [x] Add modern and down-level bounded raw-wire contract baselines.
- [x] Add client-opted MCP Tasks for keyword rebuild/classification, including polling, completion, cancellation, TTL, capacity, timeout, result bounds, health, and metrics coverage.
- [x] Make Tasks independently disableable through `Mcp:Tasks:Enabled` while retaining ordinary tool calls.
- [x] Add two experimental, disabled-by-default MCP Apps that render typed read-only contract/path data with no external network access.
- [x] Add a jsdom accessibility, CSP, external-asset, and hostile-value injection harness for both Apps.
- [x] Document shipped behavior, security boundaries, rollback controls, and known operational limits in `docs/mcp-2-capabilities.md`.

Deliberately deferred because it needs a product decision, deployment topology, or real-client evidence:

- [ ] deployed VS Code/Codex/Continue compatibility evidence and App screenshots
- [ ] additional graph-analysis structured contracts beyond `find_connection`
- [ ] durable, multi-replica, multi-principal task ownership and restart survival
- [ ] task progress phases and explicit host-shutdown drain/cancel semantics
- [ ] tool-aware rate limiting and destructive-tool authorization roles
- [ ] any future standardized parameter-header pilot, after real-client evidence confirms compatible parameter semantics
- [ ] browser-host acceptance beyond the local DOM harness

Recorded decisions:

- Text compatibility uses the existing useful Markdown, not a JSON-only response.
- Structured pilots reuse existing protocol-neutral Application contracts; MCP response construction stays in `McpServer`.
- `execute_context_workflow` is read-only because Application refuses every mutating/destructive workflow step; `allowGraphMutation` does not enable execution.
- Only `clear_project_knowledge` and `clear_code_graph` advertise destructive behavior.
- Non-destructive writes advertise conservative non-idempotency because timestamps, change counters, or generated identifiers can change on retries.
- Tasks use the stable `WithTasks` extension and a process-local singleton store with a 30-minute TTL and 500 ms polling hint.
- MCP Tasks and REST keyword jobs remain separate; the REST job's `CancellationToken.None` path is not reused.
- Both Apps render tool-provided structured data, are disabled by default, and use narrowly scoped `MCPEXP003` suppressions.
- `find_connection` now has a bounded `1.0` node/edge/path contract and a self-contained connection viewer.
- The optional `[McpHeader]` pilot is not shipped: the header is an integrity mirror of the JSON argument, and annotating an optional argument broke calls that omitted the mirror.
- Discovery uses a five-minute private cache scope. Deployment/restart and the bounded TTL are the current invalidation model.

## Executive Summary

CodeMeridian now runs the MCP 2.x SDK over stateless Streamable HTTP and has locally implemented the capability slices that fit the product:

1. describe every tool accurately with MCP safety annotations
2. add typed structured results to a small set of tools, including one graph path, while preserving markdown compatibility
3. use MCP Tasks for genuinely long-running maintenance operations, with bounded lifecycle and cancellation
4. use parsed tool identity for bounded observability; defer parameter headers and rate limiting until client/production evidence exists
5. add caching hints for stable discovery responses
6. provide optional MCP Apps that render typed read-only contract and graph-path data behind an experimental feature flag

The remaining acceptance batch is deliberately live-only: deploy once, exercise VS Code/Codex/Continue, run real graph Tasks, inspect App rendering, and capture compatibility evidence without requiring intermediate server or index updates.

## Baseline Already Completed

These items are prerequisites and are already present in the current working tree:

- [x] Reference `ModelContextProtocol.AspNetCore` with version `2.*`.
- [x] Resolve the package to stable `2.0.0`.
- [x] Run the MCP host in explicit stateless mode.
- [x] Use Streamable HTTP in generated VS Code configuration.
- [x] Retain the historical `/sse` route path to avoid unnecessary endpoint churn.
- [x] Remove stateful `IdleTimeout` configuration and long-lived SSE Kestrel tuning.
- [x] Add an in-memory integration test that connects with the 2.0 client.
- [x] Verify all currently advertised tools contain object `inputSchema` values.
- [x] Build the MCP host with zero MCP SDK warnings.

## Original Problem Statement

At the start of this plan, the transport upgrade alone did not expose the full value of MCP 2:

- all 62 tools were registered primarily with a name and description
- clients did not receive complete read-only, destructive, idempotency, or open-world hints
- the repository convention assumed `Task<string>` Markdown responses for every tool
- machine clients had to parse Markdown to recover facts that already existed as typed data internally
- long-running keyword maintenance calls were synchronous through MCP even though a background job abstraction already existed for REST
- the large, stable tool catalog had no intentional discovery caching contract
- standardized MCP metadata and parsed request context were not used for per-tool telemetry or operational controls
- the existing GraphQL/client-extension direction had no standardized embedded MCP UI

## Goals

- [x] Make client approval and safety behavior match what each tool actually does.
- [x] Provide machine-readable facts without removing the markdown experience agents already use.
- [x] Give selected long-running operations standard lifecycle, polling, timeout, result bounds, and cancellation semantics.
- [ ] Add meaningful phase/count/percentage progress when the underlying keyword operations expose it.
- [x] Reduce repeated discovery overhead for the stable 62-tool catalog.
- [x] Improve per-tool observability without logging secrets or full arguments.
- [x] Offer an optional read-only MCP App pilot; retain the interactive graph view as a follow-up.
- [x] Preserve stateless HTTP and current endpoint compatibility.
- [x] Keep protocol dependencies in `McpServer` and out of `Core` and `Application`.
- [x] Keep all tool outputs factual and deterministic; do not add server-side LLM reasoning.

## Non-Goals

- [x] Do not restore legacy stateful SSE behavior.
- [x] Do not add deprecated Roots, Sampling, or Logging APIs to the ordinary stateless tool path.
- [x] Do not build a server-side multi-agent runtime.
- [x] Do not move Cypher out of `src/Infrastructure/Graph/`.
- [x] Do not expose raw Cypher execution.
- [x] Do not convert all 62 tools to structured output in one change.
- [x] Do not require Tasks support from every client.
- [x] Do not make the experimental MCP Apps package a hard dependency for core graph queries.
- [x] Do not use MCP headers as the sole authorization boundary.
- [x] Do not cache graph query results without an explicit invalidation model.
- [x] Do not place MCP SDK types in `Core` or `Application` contracts.

## Architecture Constraints

The implementation must preserve:

```text
McpServer -> Application -> Core
McpServer -> Infrastructure through dependency injection
Infrastructure -> Core
Core -> nothing
```

Protocol-specific concerns belong in `src/McpServer`:

- MCP attributes and annotations
- `CallToolResult` construction
- Tasks registration and task-store adapters
- MCP Apps resources and UI metadata
- MCP request filters
- HTTP header extraction, rate limiting, and protocol telemetry

Application concerns remain protocol-neutral:

- graph result models
- background operation orchestration
- cancellation and progress abstractions
- deterministic markdown formatting
- client-extension metadata

Infrastructure concerns remain adapter-specific:

- Neo4j queries and writes
- durable task storage if selected later
- persistence and query performance

## Verified Starting Surfaces

This section records the pre-adoption seams used to select surgical implementation targets. The live implementation status is recorded near the top of this document.

### MCP composition and tools

- `src/McpServer/Program.cs` registers stateless HTTP and all tool classes.
- `src/McpServer/Tools/CodebaseTools.cs` exposes the primary graph/query surface.
- `src/McpServer/Tools/CodebaseTools.Analytics.cs` exposes impact, test, architecture, and trace tools.
- `src/McpServer/Tools/CodebaseTools.Gds.cs` exposes graph-data-science tools.
- `src/McpServer/Tools/CodebaseTools.ContextWorkflows.cs` exposes workflow planning and execution.
- `src/McpServer/Tools/KnowledgeTools.cs` exposes graph/document mutations and destructive clears.
- `src/McpServer/Tools/KeywordTools.cs` exposes keyword queries and maintenance operations.
- `src/McpServer/Tools/ClientExtensionTools.cs` exposes the GraphQL client-extension contract.

### Existing long-running job seam

- `src/Application/Services/IKeywordGraphJobService.cs` defines rebuild, classify, and status operations.
- `src/Application/Services/KeywordGraphJobService.cs` holds singleton in-memory job state.
- `src/McpServer/Api/KnowledgeApiEndpoints.cs` exposes REST submission and status polling.
- the starting MCP keyword tools called `IKeywordGraphService` directly and waited for completion
- the existing job service launches work with `CancellationToken.None`, so its current REST background path does not provide cooperative cancellation

### Existing client UI/read seam

- `/graphql` is a bounded read-only graph surface.
- `ClientExtensionTools` already describes how clients can build behavior against GraphQL.
- `docs/graphql/` contains checked-in query examples.
- `companions/CodeMeridian.Evolution` contains a richer UI that may inform an MCP App, but should not automatically become an MCP dependency.

### Current contract convention

- `docs/agent/conventions.md` originally directed MCP tools to return `Task<string>` Markdown for every case.
- Application services commonly format markdown directly.
- tests primarily assert formatted markdown and wrapper argument forwarding.

## Decision Gates

No phase should start until its gate is resolved and recorded in this document or a linked ADR.

### Gate A: Compatibility contract

- [x] Confirm that every structured-output pilot must continue returning useful text content.
- [x] Use the existing useful Markdown for the current pilots.
- [x] Reuse the existing client-extension contract version for the first structured object; do not invent a shared MCP result version yet.
- [x] Confirm behavior for an SDK client pinned to `2025-11-25`, including initialize, discovery, annotations, structured text fallback, ordinary maintenance calls, and cache hints.
- [ ] Confirm the minimum supported versions of VS Code, Codex, and Continue for each optional capability.

### Gate B: Tasks scope

- [x] Use `WithTasks` with a selector so only keyword maintenance accepts client-opted Tasks.
- [x] Use stable Tasks APIs; no `MCPEXP002` suppression is required.
- [x] Use the SDK's in-memory store for the first release.
- [x] Document that task results do not survive server restarts.
- [x] Keep REST keyword jobs and MCP Tasks separate until shared orchestration is proven useful.

### Gate C: MCP Apps scope

- [ ] Confirm which supported clients can render MCP Apps.
- [x] Ship the App disabled by default.
- [x] Render tool-provided structured data only; do not call GraphQL from the first App.
- [x] Prefer tool-provided data for the first slice to avoid browser-token and CORS complexity.
- [x] Scope `MCPEXP003` suppressions narrowly to Apps registration, metadata, and resources.

### Gate D: Cache policy

- [x] Use a five-minute `tools/list` TTL.
- [x] Use `Private` cache scope.
- [x] Use `Private` because discovery is authenticated and can vary by feature configuration.
- [x] Treat deployment/restart plus the bounded five-minute TTL as the current invalidation model.

## Phase 0: Baseline Inventory And Contract Lock

### 0.1 Capture protocol and client baselines

- [x] Record the exact resolved MCP package versions (`2.0.0` for the base server and both extension packages).
- [x] Record the negotiated protocol version from the in-memory integration client (`2026-07-28` modern and `2025-11-25` pinned).
- [x] Capture and assert the raw modern `server/discover` exchange.
- [x] Capture and assert raw `tools/list` contract fields for `2026-07-28`.
- [x] Capture a down-level `2025-11-25` initialization and `tools/list` response.
- [x] Store bounded golden snapshots for stable contract facts.
- [x] Exclude descriptions from strict snapshots so normal documentation edits do not create noise.
- [x] Assert stable tool names and required schemas separately from display text.

### 0.2 Inventory every tool

- [ ] Generate a deterministic inventory containing tool name, declaring method, file, read/write classification, destructive risk, idempotency, open-world behavior, return type, and expected duration.
- [x] Fail a contract test when a registered tool is missing from the inventory.
- [x] Fail a contract test when the inventory contains a tool no longer registered.
- [x] Keep the inventory test data near `CodeMeridian.McpServer.Tests` rather than duplicating it in production.

### 0.3 Measure current behavior

- [ ] Measure serialized `tools/list` payload size.
- [ ] Measure cold and warm `tools/list` latency.
- [ ] Measure representative tool-result sizes for compact, full, and source-snippet modes.
- [ ] Measure `rebuild_keyword_graph` and `classify_keywords` duration on small and production-sized graphs.
- [ ] Record timeout behavior in VS Code, Codex, and Continue.
- [ ] Record whether each client consumes structured content, Tasks, caching hints, and MCP Apps.

### 0.4 Lock regression tests

- [x] Preserve useful Markdown for every structured-output pilot.
- [x] Add explicit cancellation tests for the task-enabled keyword service path.
- [x] Add raw-wire and GET-path protocol tests proving the server remains stateless.
- [x] Add a test proving no standalone legacy SSE GET endpoint is required.
- [x] Add tests proving authentication applies to `server/discover`, `tools/list`, `tools/call`, `tasks/get`, `tasks/update`, and `tasks/cancel`.

### 0.5 Close the 2.0 breaking-change audit

Record each release-note item as **applicable**, **already handled**, **client-only**, or **not applicable**, with a link to the validating test or code location. Do not rely on package restore and compilation alone as migration proof.

#### Server transport and negotiation

- [x] Mark stateless-by-default HTTP as handled by explicit `options.Stateless = true` configuration in `src/McpServer/Program.cs`.
- [x] Verify no CodeMeridian feature depends on transport sessions, standalone SSE GET/DELETE endpoints, or unsolicited server-to-client requests.
- [x] Verify no stateful-only transport option remains and the build emits no `MCP9006` diagnostic.
- [x] Verify the 2.0 integration client connects through modern discovery and exercises the server.
- [x] Verify an SDK client pinned to `2025-11-25` falls back to its initialize handshake.
- [x] Document that `/sse` is a retained route name for Streamable HTTP and does not imply legacy stateful SSE behavior.

#### Deprecated base-protocol capabilities

- [x] Confirm CodeMeridian does not register or invoke MCP Roots, Sampling, or Logging APIs.
- [x] Distinguish ordinary `Microsoft.Extensions.Logging`/diagnostic instrumentation from the deprecated MCP Logging capability.
- [x] Keep `MCP9005` as an explicit warning-as-error; do not suppress it unless a separately approved down-level requirement appears.
- [x] Use the extension model and Tasks for new interaction instead of reintroducing deprecated APIs.

#### Tasks extraction

- [x] Mark the Tasks API move as not applicable because the repository did not use the v1.4 experimental Tasks implementation.
- [x] Treat Phase 5 as a new feature using `ModelContextProtocol.Extensions.Tasks`, not as a namespace-only migration.
- [x] Do not reference removed preview helpers such as `CreateMcpTaskScope`.
- [x] Reference the Tasks extension with the same `2.*` policy; restore resolves it to `2.0.0`.

#### Structured-result wire shape

- [x] Confirm the pre-pilot tools did not advertise structured content; restrict adoption to the reviewed client-extension slice.
- [x] Use object-shaped structured contracts except where the complete result is intentionally an example array.
- [ ] Add a test proving a non-object structured result, if ever introduced, is emitted as the raw value and is not assumed to be wrapped in `{ "result": ... }`.
- [x] Emit raw object/array structured values with no invented `{ "result": ... }` wrapper.

#### Required tool input schemas

- [x] Keep the existing integration assertion that every advertised tool contains an object `inputSchema`.
- [x] Audit the in-repository endpoint fixtures and bounded wire snapshots for tool payloads that omit `inputSchema`.
- [x] Retain SDK-generated object schemas for parameterized and parameterless tools.
- [x] Add a negative fixture proving a missing `inputSchema` is rejected, so future tests model SDK 2 behavior accurately.

#### OAuth and dynamic client registration

- [x] Record OAuth callback migration as not applicable while CodeMeridian uses its server-side API-key scheme and contains no OAuth client/provider.
- [x] Treat issuer, PKCE, scope-escalation, and dynamic-registration changes as future OAuth client/auth-server concerns rather than server-tool changes.
- [ ] If OAuth is introduced later, require `AuthorizationCallbackHandler`, propagation of authorization code/state/issuer, advertised `S256`, issuer equality, bounded step-up behavior, and an explicitly reviewed dynamic-registration application type.
- [ ] Do not weaken issuer or PKCE checks to accommodate a non-conformant identity provider; correct its discovery metadata.

#### Client transport error handling

- [x] Record propagated SSE connection exceptions as client-only for the current server project.
- [ ] Search any future in-repository MCP client for logic that catches only `IOException("Failed to connect transport.")`.
- [ ] When client code exists, handle the underlying `HttpRequestException`, `TimeoutException`, and genuine I/O failures without depending on the former wrapper text.

### Phase 0 exit criteria

- [x] Every stable 2.0 breaking-change category has an applicability disposition in this plan.
- [x] Implemented items have code, test, or operational-document references.
- [x] No obsolete-warning suppression was introduced merely to make the upgrade compile.
- [x] Modern and down-level protocol baselines are captured and permanently regression-tested.

## Phase 1: Tool Safety And Behavioral Annotations

This is the lowest-risk and highest-confidence capability slice.

### 1.1 Annotation rules

For every `[McpServerTool]`:

- [x] Set `ReadOnly = true` only when the tool cannot mutate graph, documents, configuration, files, or external state.
- [x] Set `Destructive = true` whenever the tool can delete or irreversibly replace data.
- [x] Set `Idempotent = true` only after verifying repeated identical calls do not create additional effects.
- [x] Set `OpenWorld = false` for operations restricted to CodeMeridian's configured graph and checked-in data.
- [x] Leave `OpenWorld = true` only for tools that contact an unpredictable external service; none currently do.
- [x] Add a human-readable `Title` for every tool.
- [x] Do not mark computational expense as a side effect; expensive read-only queries remain read-only.
- [x] Treat a tool as mutating if its implementation can actually reach a conditional mutation.

### 1.2 Read-only tool checklist

Mark these as proposed `ReadOnly = true` and `OpenWorld = false` after confirming the implementation path:

- [x] `query_codebase`
- [x] `get_architectural_overview`
- [x] `search_documentation`
- [x] `find_tool_dependency_impact`
- [x] `find_impact`
- [x] `find_diagnostics`
- [x] `find_diagnostics_for_node`
- [x] `find_stale_knowledge`
- [x] `knowledge_decay`
- [x] `find_implementation_surface`
- [x] `analyze_feature_implementation_path`
- [x] `plan_edit_route`
- [x] `replace_surface`
- [x] `resolve_exact_symbol`
- [x] `check_graph_freshness`
- [x] `find_graph_drift`
- [x] `plan_context_workflow`
- [x] `find_config_definitions`
- [x] `find_config_usage`
- [x] `find_hotspots`
- [x] `find_frontend_cascade_conflicts`
- [x] `find_connection`
- [x] `trace_endpoint`
- [x] `find_unreferenced`
- [x] `find_cross_project_dependencies`
- [x] `find_coverage_gaps`
- [x] `find_test_shield`
- [x] `find_recently_changed`
- [x] `find_large_nodes`
- [x] `get_context_for_editing`
- [x] `build_minimal_context`
- [x] `find_god_classes`
- [x] `find_downstream`
- [x] `find_cycles`
- [x] `architecture_drift_history`
- [x] `find_architecture_violations`
- [x] `find_smell_paths`
- [x] `find_high_churn`
- [x] `analyze_changed_subgraph`
- [x] `get_pagerank`
- [x] `get_betweenness`
- [x] `find_bridges`
- [x] `find_natural_modules`
- [x] `suggest_extractions`
- [x] `suggest_responsibility_slices`
- [x] `find_similar_nodes`
- [x] `hybrid_search`
- [x] `find_implementation_patterns`
- [x] `find_duplicate_candidates`
- [x] `find_related_knowledge`
- [x] `get_client_extension_contract`
- [x] `list_client_extension_examples`
- [x] `get_client_extension_example`
- [x] `execute_context_workflow` (verified read-only because mutating/destructive workflow steps are rejected)

### 1.3 Mutating and destructive tool checklist

- [x] `ingest_code_node`: `ReadOnly = false`; conservatively keep `Idempotent = false` because repeated calls update observable state.
- [x] `ingest_relationship`: `ReadOnly = false`; conservatively keep `Idempotent = false` because retry semantics are not guaranteed.
- [x] `ingest_document`: `ReadOnly = false`; conservatively keep `Idempotent = false` because replacement timestamps/change state can differ.
- [x] `link_external_concept`: `ReadOnly = false`; conservatively keep `Idempotent = false` because generated identity/edge effects are not guaranteed stable.
- [x] `rebuild_keyword_graph`: `ReadOnly = false` and `Idempotent = false` because rebuild timestamps/counts are observable.
- [x] `classify_keywords`: `ReadOnly = false` and `Idempotent = false` because classification writes are observable.
- [x] `clear_project_knowledge`: `ReadOnly = false`, `Destructive = true`, and `Idempotent = true`.
- [x] `clear_code_graph`: `ReadOnly = false`, `Destructive = true`, and `Idempotent = true`.
- [x] Correct the proposed `execute_context_workflow` classification: it is read-only because Application refuses every mutating step.
- [x] Verify `execute_context_workflow` cannot reach a destructive step and advertise `Destructive = false`.
- [x] Set `OpenWorld = false` for every mutation because it only touches CodeMeridian-controlled state.

### 1.4 Annotation contract tests

- [x] Enumerate tools through the real MCP `tools/list` endpoint.
- [x] Assert all 62 tools declare an intentional read-only value.
- [x] Assert the known destructive set exactly matches the approved inventory.
- [x] Assert mutating tools are never advertised as read-only.
- [x] Assert local graph tools are not advertised as open-world.
- [x] Assert tool names remain unchanged.
- [x] Assert annotation changes work for modern and down-level clients.

### 1.5 Documentation updates

- [x] Update `docs/agent/conventions.md` with annotation requirements.
- [x] Update the "adding a new graph tool" checklist to require a classification.
- [x] Document that annotations are behavioral hints, not authorization.
- [x] Add a contributor example for one read-only and one destructive tool.

### Phase 1 exit criteria

- [x] Every registered tool has reviewed safety metadata.
- [x] The inventory and live tool list agree exactly.
- [x] Destructive tools are accurately advertised.
- [x] Existing tool behavior and names are unchanged.
- [x] MCP server tests pass for both negotiated protocol eras locally.

## Phase 2: Structured Tool Result Foundation

### 2.1 Compatibility contract

- [x] Preserve a useful Markdown `TextContentBlock` for existing clients.
- [x] Add `StructuredContent` for clients that consume machine-readable results.
- [x] Advertise an accurate SDK-generated `outputSchema`.
- [x] Preserve the existing `v1` version on the primary client-extension contract object.
- [x] Do not require clients to parse Markdown to obtain the pilot facts.
- [x] Do not force human-facing Markdown consumers to render raw JSON.
- [x] Construct and test both representations explicitly instead of assuming `UseStructuredContent` supplies both.

### 2.2 Layering design

- [x] Keep MCP `CallToolResult` and protocol attributes in `McpServer`.
- [x] Reuse protocol-neutral Application records because the same client-extension facts already serve non-MCP callers.
- [x] Keep markdown rendering in Application when it represents the canonical application result.
- [x] Keep typed query facts separate from MCP Markdown formatting for each implemented pilot.
- [x] Do not parse existing Markdown back into DTOs.
- [x] Do not create a universal result hierarchy before repetition is proven.
- [x] Keep one public result type per file.
- [x] Keep the reused records small and in a feature-specific namespace.

### 2.3 Candidate common metadata

Evaluate, but do not automatically standardize, these fields:

- [x] `contractVersion` (adopted only for the independently evolving `find_connection` contract; no universal metadata base type)
- [ ] `summary`
- [ ] `projectContext`
- [ ] `confidence`
- [ ] `relationshipCompleteness`
- [ ] `staleWarning`
- [ ] `warnings`
- [ ] `facts`
- [ ] `evidence`
- [ ] `suggestedFiles`
- [ ] `suggestedTests`
- [ ] `nextActions`

No common metadata type was introduced. The first slice proved that existing feature-specific contracts can be surfaced directly without a universal MCP result hierarchy.

### Implemented pilot: client-extension discovery

The initial lower-risk vertical slice uses three existing typed client-extension contracts:

- [x] `get_client_extension_contract` returns a versioned object plus compatible Markdown.
- [x] `list_client_extension_examples` returns the complete example array plus compatible Markdown.
- [x] `get_client_extension_example` returns one typed example plus compatible Markdown.
- [x] Advertise `outputSchema` for these three tools.
- [x] Serialize with the MCP SDK's configured camel-case options.
- [x] Keep non-pilot tools on their pre-existing result contracts.
- [x] Test typed properties and the Markdown fallback through both wrappers and the real endpoint.

Common metadata must not erase tool-specific schema. For example, an impact path should remain a typed path rather than an unstructured `facts` dictionary.

### Implemented graph pilot: connection analysis

`find_connection` was selected as the bounded graph pilot after graph impact and test-shield review:

- [x] Add protocol-neutral `ConnectionAnalysisResult`, `ConnectionNodeResult`, and `ConnectionEdgeResult` records in Application, one public type per file.
- [x] Give the result an explicit `contractVersion: "1.0"`.
- [x] Return bounded ordered nodes, ordered edges, hop state, truncation state, and frontend signals.
- [x] Exclude source snippets and arbitrary graph property dictionaries.
- [x] Produce the existing useful Markdown from the same typed result rather than parsing Markdown.
- [x] Preserve summary, empty-path, current outgoing-edge alignment, and legacy incoming-edge alignment behavior.
- [x] Advertise the SDK-generated output schema and return `structuredContent` plus text.
- [x] Test typed facts, Markdown parity, empty arrays, nullability, schema validity, payload limits, and down-level text fallback.

### 2.4 Pilot 1: graph freshness

Suggested target:

- `check_graph_freshness`

Tasks:

- [ ] Define a typed freshness result with project, query, trust summary, relationship completeness, index timestamps, and node findings.
- [ ] Move result construction ahead of markdown formatting.
- [ ] Retain the existing markdown formatter.
- [ ] Enable structured content and advertise the typed output schema.
- [ ] Add schema snapshot coverage.
- [ ] Test zero matches, mixed confidence, stale nodes, and low relationship completeness.
- [ ] Verify bounded samples and no source-body leakage.

### 2.5 Pilot 2: impact analysis

Suggested target:

- `find_impact`

Tasks:

- [ ] Define typed impacted nodes and path segments.
- [ ] Preserve proven, heuristic, and unknown-risk classifications.
- [ ] Preserve depth and truncation metadata.
- [ ] Preserve the existing markdown summary.
- [ ] Verify cyclic paths serialize without recursive object loops.
- [ ] Test empty impact, direct callers, transitive callers, stale evidence, and bounded depth.

### 2.6 Pilot 3: test shield

Suggested target:

- `find_test_shield`

Tasks:

- [ ] Define typed direct, primary, secondary, and unshielded findings.
- [ ] Include suggested commands as data, not only prose.
- [ ] Include confidence and evidence path fields.
- [ ] Preserve the current markdown sections.
- [ ] Test directly linked, heuristic-only, and no-shield cases.

### 2.7 Pilot 4: minimal context

Suggested target:

- `build_minimal_context`

Tasks:

- [ ] Define typed callers, callees, impacts, downstream dependencies, tests, gaps, files, snippets, and token-budget metadata.
- [ ] Keep source snippets bounded.
- [ ] Ensure structured output respects `maxTokens` or define a separate structured-size bound.
- [ ] Avoid duplicating large snippets in both text and structured content.
- [ ] Define truncation flags per collection.
- [ ] Test compact, full, snippets-on, snippets-off, and budget-exceeded paths.

### 2.8 Structured-content contract testing

- [x] Assert `outputSchema` is present for pilot tools.
- [x] Validate every implemented structured response against its advertised schema with JsonSchema.Net.
- [x] Assert non-pilot tools do not advertise a structured output schema.
- [x] Assert text content remains non-empty.
- [x] Assert a `2025-11-25` client can call a structured pilot and retain useful text.
- [x] Assert JSON property naming is stable and camelCase.
- [x] Assert the primary structured contract retains version `v1`.
- [x] Add a 128 KiB structured pilot payload limit.
- [x] Add nullability and empty-collection tests; serialize declared nullable properties so schemas and payloads agree.
- [ ] Add serialization tests under trimming/NativeAOT only if the deployment uses those modes.

### 2.9 Expansion gate

- [ ] Collect client compatibility results from VS Code, Codex, and Continue.
- [ ] Compare token usage before and after structured output.
- [ ] Confirm the structured form is actually consumed by target clients.
- [x] Confirm the graph pilot remained surgical: three small result records and one typed Application method, with no universal hierarchy or MCP dependency.
- [x] Reject a shared result metadata type for now; the two feature contracts have different version and fact needs.
- [ ] Select the next maximum five tools; do not mass-convert the remaining catalog.

### Phase 2 exit criteria

- [x] Four tools, including the high-value `find_connection` graph pilot, return schema-valid structured facts and useful Markdown.
- [x] No MCP SDK types leak into Application or Core.
- [x] Existing clients retain working text results for the implemented pilots.
- [x] Implemented schema contracts are versioned and validated against their advertised schemas.
- [ ] The team has evidence to decide whether broader conversion is worthwhile.

## Phase 3: Standardized Headers, Telemetry, And Rate Limits

### 3.1 Observe standardized MCP headers

SDK 2 clients can send:

- `Mcp-Method`
- `Mcp-Name`
- `Mcp-Param-*` for parameters annotated with `[McpHeader]`

Tasks:

- [ ] Confirm which supported clients send standardized headers.
- [x] Add bounded logging for the parsed registered tool name, duration, and outcome.
- [x] Add Activity tags for registered tool name, bounded category, and outcome.
- [x] Add a duration histogram by bounded tool category and outcome.
- [x] Add a call counter with bounded `success`, `tool_error`, `cancelled`, `failure`, `timeout`, and `result_too_large` outcomes.
- [x] Never log API keys, full request bodies, document content, query text, source snippets, or raw arguments.
- [x] Normalize unmatched calls to the bounded `unknown` category instead of an attacker-controlled metric label.

### 3.2 Project-context header experiment and decision

- [x] Select `query_codebase` for an initial read-only experiment.
- [x] Verify through the real endpoint that `[McpHeader]` is an integrity mirror of the JSON value, not an alternate argument source.
- [x] Detect that annotating optional `projectContext` broke ordinary modern calls that omitted the mirrored header.
- [x] Remove `[McpHeader("ProjectContext")]` and assert `x-mcp-header` is absent from the optional parameter schema.
- [x] Keep the JSON argument canonical and retain existing wrapper forwarding behavior.
- [x] Validate JSON project context before Application execution: maximum 200 characters and no control characters.
- [x] Confirm modern and down-level clients work when optional project context and any mirrored header are absent.
- [x] Keep parameter headers outside routing, metrics, enforcement, and authorization.
- [ ] Revisit a different standardized-header pilot only after supported-client evidence and compatible required/optional semantics exist.

### 3.3 Tool-aware rate limiting

Define categories:

- read-only/light
- read-only/heavy
- mutating
- destructive
- task lifecycle

Tasks:

- [ ] Build the category map from the reviewed annotation inventory.
- [ ] Apply stricter concurrency and request limits to heavy graph algorithms.
- [ ] Apply low burst limits to destructive tools.
- [ ] Keep ordinary graph queries usable during a maintenance task.
- [ ] Return standards-compliant HTTP/JSON-RPC errors.
- [ ] Verify the limiter cannot be bypassed by spoofing `Mcp-Name`.
- [ ] Reconcile the header with the parsed JSON-RPC method before making security-sensitive decisions.
- [ ] Document reverse-proxy limits that should match the application policy.

### 3.4 Authorization filters

- [ ] Evaluate `AddAuthorizationFilters()` for tool-level policy support.
- [x] Keep the existing authenticated endpoint boundary.
- [x] Defer a separate admin credential until CodeMeridian has a real multi-principal use case.
- [x] Do not add role complexity until there is a real multi-principal use case.
- [ ] Add denial tests before enabling any new policy.

### Phase 3 exit criteria

- [x] Per-tool telemetry records bounded category/outcome dimensions and exact registered tool names only in logs/activities.
- [x] No sensitive arguments are read into logs or metrics by the filter.
- [ ] Rate limits are based on verified request identity, not an untrusted header alone.
- [x] Optional project-context calls remain backward compatible because the incompatible header annotation is not shipped.

## Phase 4: Discovery Caching Hints

### 4.1 Tool-list cache policy

The 62-tool catalog is static for the process lifetime unless future feature flags or authorization filtering alter it.

- [x] Add a `tools/list` request filter.
- [x] Set a positive `TimeToLive` on `ListToolsResult`.
- [x] Set `CacheScope` intentionally.
- [x] Start with a conservative five-minute TTL.
- [x] Use `Private` because discovery is authenticated and feature configuration can vary.
- [x] Keep runtime visibility static within one process; use feature flags only at startup.
- [x] Use deployment/restart and the bounded TTL as the current invalidation strategy.

### 4.2 Cache-hint tests

- [x] Call the lower-level client `ListToolsAsync(ListToolsRequestParams)` overload.
- [x] Assert the five-minute TTL and private cache scope.
- [x] Assert all 62 tool definitions remain present.
- [x] Assert the pinned `2025-11-25` path receives the same bounded private cache policy.
- [x] Assert unauthorized callers receive `401` and no public cacheable tool list.
- [x] Keep the same private cache policy when startup feature flags alter extension capabilities.

### 4.3 Resource caching

If MCP Apps or resources are added:

- [ ] Set long TTLs for immutable hashed UI assets.
- [ ] Set short or zero TTLs for dynamic GraphQL-derived resources.
- [ ] Use content hashes in resource URIs where practical.
- [ ] Do not cache authenticated graph data publicly.
- [ ] Test stale resource behavior after deployment.

### Phase 4 exit criteria

- [x] Modern clients receive explicit, safe discovery caching hints.
- [x] Private scope prevents shared reuse of an authenticated tool catalog.
- [x] No graph query result is cached without a separate approved design.

## Phase 5: MCP Tasks For Long-Running Operations

### 5.1 Client and SDK spike

- [x] Add `ModelContextProtocol.Extensions.Tasks` with the same 2.x version policy.
- [x] Exercise `WithTasks` through the real in-memory MCP host rather than a less representative test-only server.
- [x] Verify ordinary `tools/call` behavior when the client does not opt into Tasks.
- [x] Verify `CallToolWithPollingAsync` with the 2.0 client.
- [x] Verify unauthenticated `tasks/get`, `tasks/update`, and `tasks/cancel` requests are rejected.
- [x] Verify down-level clients receive ordinary results when they do not opt into Tasks.
- [ ] Record which real clients expose Tasks today.

### 5.2 Task-store decision

- [x] Use a singleton SDK in-memory store under stateless HTTP.
- [x] Accept and document in-memory loss on restart for the first single-process release.
- [x] Defer durability; require any future durable store to remain outside the code knowledge graph.
- [x] Wrap the SDK-provided concurrent in-memory store for operational bounds without replacing its terminal transition semantics.
- [x] Require and test idempotent terminal transitions.
- [x] Configure a 30-minute task TTL.
- [x] Verify immediate read/poll after task creation.
- [x] Document that the in-memory store is not suitable for multiple server replicas.
- [x] Define a configurable global maximum of four active tasks per process by default.
- [ ] Define per-principal capacity when CodeMeridian supports more than one authenticated identity.

### 5.3 First task candidates

Prioritize:

- `rebuild_keyword_graph`
- `classify_keywords`

Evaluate later:

- `execute_context_workflow`
- expensive graph-data-science tools after measurement

Do not task-enable merely because a method is asynchronous.

### 5.4 Keyword task implementation

- [x] Keep `IKeywordGraphService` protocol-neutral.
- [x] Ensure the tool cancellation token reaches `IKeywordGraphService` and its Neo4j operations.
- [x] Do not reuse the REST fire-and-forget `CancellationToken.None` path for MCP Tasks.
- [x] Keep rebuild and classification as distinct tools/tasks.
- [ ] Expose phases: preparing, rebuilding, classifying, summarizing.
- [ ] Report bounded progress percentages only when the denominator is meaningful.
- [ ] Otherwise report phase and completed-count progress.
- [x] Propagate `tasks/cancel` cooperatively.
- [ ] Ensure cancellation does not leave partially replaced keyword data in an invalid state.
- [x] Advertise both maintenance writes conservatively as non-idempotent; clients should not blindly retry them.
- [x] Keep the current maintenance final result as bounded text because these tools do not advertise structured output; require a typed task result before enabling one later.

### 5.5 REST job coexistence

- [x] Keep `KeywordGraphJobService` as the REST-only job mechanism.
- [x] Avoid making Application depend on `IMcpTaskStore`.
- [x] Do not extract shared orchestration until meaningful sequencing duplication appears.
- [x] Keep REST status URLs stable.
- [x] Defer REST cancellation as an independent follow-up.
- [x] Document that REST job IDs and MCP task IDs are distinct.

### 5.6 Task lifecycle tests

- [x] Client does not opt in: tool returns an ordinary result.
- [x] Client opts in for keyword maintenance: tool returns a task.
- [x] `tasks/get` immediately finds the created task.
- [x] Running task transitions to completed.
- [x] Domain/tool errors complete with `isError` rather than protocol `failed`.
- [x] Direct protocol/store failures transition to `failed`.
- [x] Cancellation signals the tool token and the task reaches `cancelled`.
- [x] Late cancellation cannot overwrite a completed result.
- [x] Duplicate terminal updates are no-ops.
- [x] Store TTL removes expired tasks.
- [x] Unauthorized task reads, updates, and cancels are rejected.
- [ ] One principal cannot access another principal's task.
- [x] Document process-local/restart-loss behavior; automated restart testing remains unnecessary until durability is claimed.
- [ ] Two concurrent rebuilds for the same project follow the existing lease/conflict policy.
- [ ] Rebuilds for independent projects follow the approved concurrency policy.

### 5.7 Operational protections

- [x] Add active-task gauges, duration metrics, and bounded rejection counters.
- [x] Add a configurable hard maximum maintenance duration (30 minutes by default).
- [x] Add a configurable bounded result-size policy (128 KiB by default).
- [ ] Add shutdown handling and document whether tasks drain or cancel.
- [x] Add task-store capacity, TTL, storage mode, and result bounds to the `mcp_tasks` health check.
- [x] Add runbook guidance for timeout, capacity, stuck-task, restart-loss, and rollback behavior in `docs/mcp-2-capabilities.md`.

### Phase 5 exit criteria

- [x] Keyword maintenance works as ordinary calls and client-opted Tasks.
- [x] Cancellation reaches the underlying operation.
- [ ] Task state is isolated by authenticated principal.
- [x] Persistence and restart semantics are explicit.
- [x] No MCP package dependency crosses into Application or Core.

## Phase 6: MCP Apps Interactive Graph Spike

The C# MCP Apps surface is experimental under `MCPEXP003`. Treat this as a separately releasable spike.

### 6.1 Product hypothesis

An embedded interactive view should make relationship-heavy results easier to understand than markdown alone.

Candidate tools:

- `find_connection`
- `find_impact`
- `trace_endpoint`
- `find_test_shield`
- `find_natural_modules`
- `analyze_changed_subgraph`

### 6.2 Spike constraints

- [x] Add `ModelContextProtocol.Extensions.Apps` behind `Mcp:Apps:Enabled` (default `false`).
- [x] Call `WithMcpApps()` only when the feature is enabled.
- [x] Keep the underlying tool callable without the App.
- [x] Do not require a browser or UI-capable client for factual results.
- [x] Do not expose GraphQL credentials or the server API key to the iframe.
- [x] Render structured data supplied by the tool result first.
- [x] Use dedicated `ui://code-meridian/client-extension-contract` resource URI.
- [x] Keep the App asset small and self-contained.
- [x] Declare empty external connection, resource, frame, and base-URI allowlists.

### 6.3 Implemented first app: client-extension contract viewer

The safer first slice uses existing typed, bounded data rather than parsing graph Markdown:

- [x] Attach `[McpAppUi]` to `get_client_extension_contract`.
- [x] Serve `ui://code-meridian/client-extension-contract` as `text/html;profile=mcp-app`.
- [x] Render version, endpoint, limits, authentication contract, documentation paths, and example IDs from `structuredContent`.
- [x] Use DOM `textContent` rather than inserting contract values as HTML.
- [x] Allow refresh only through the same read-only tool.
- [x] Preserve the tool's ordinary Markdown and structured result when Apps are disabled or unsupported.

### 6.3 follow-up implemented: connection/path viewer

Suggested tool:

- `find_connection`

Tasks:

- [x] Complete the structured-result pilot for `find_connection` first.
- [x] Define a bounded node/edge/path payload.
- [x] Add a `ui://code-meridian/connection-viewer` resource.
- [x] Attach `[McpAppUi]` metadata to the tool.
- [x] Render path order, node type, namespace, file, relationship type, summary, and frontend signals.
- [ ] Add confidence/evidence fields only when the underlying path contract can provide factual values.
- [x] Provide a text-only accessible representation with semantic ordered lists and status announcements.
- [x] Handle empty and truncated paths.
- [x] Exclude arbitrary graph properties and source snippets from the payload.
- [x] Render every graph-controlled value through DOM `textContent`.

### 6.4 Second app candidate: impact explorer

- [ ] Reuse only proven UI primitives from the connection viewer.
- [ ] Display direction, hop depth, confidence, and test shields.
- [ ] Visually distinguish proven and heuristic edges.
- [ ] Make truncation explicit.
- [ ] Keep layout deterministic for snapshot testing.
- [ ] Do not add live mutation controls.

### 6.5 GraphQL integration decision

- [ ] Decide whether a later app may call `/graphql`.
- [ ] If yes, define a browser-safe auth flow that never embeds the server API key.
- [ ] Define CORS and dedicated-origin requirements.
- [ ] Reuse GraphQL depth, complexity, and page-size bounds.
- [ ] Add per-app query allowlists if practical.
- [ ] Keep GraphQL read-only.

### 6.6 Apps security review

- [x] Review and test empty external CSP domain allowlists.
- [x] Request no browser sandbox permissions.
- [x] Leave iframe origin/sandbox enforcement to the MCP host as required by the Apps specification; the server serves only the declared resource.
- [x] Verify no API-key names, authorization headers, or secrets enter the HTML resource.
- [x] Avoid graph-controlled text in this pilot and render all structured values through `textContent`.
- [x] Read one fixed filename beneath `AppContext.BaseDirectory/Apps`; accept no path input.
- [x] Verify the App asset references no destructive tool and refreshes only through the read-only contract tool.
- [x] Explicitly advertise and verify both `model` and `app` visibility because the model calls each tool and each read-only App may refresh it.

### 6.7 Apps compatibility and tests

- [x] Test `resources/list` and `resources/read` for the UI resource.
- [x] Test tool `_meta.ui` serialization.
- [x] Test the app-disabled configuration.
- [x] Test non-App/default clients still receive normal results.
- [x] Render both UIs in a jsdom browser-DOM test harness.
- [x] Add semantic accessibility and keyboard-operability checks.
- [x] Add CSP, external-asset, forbidden-sink, and hostile-value injection regression tests.
- [ ] Record client support and screenshots in the plan evidence.

### 6.8 Experimental dependency policy

- [x] Do not tolerate global `MCPEXP003` suppression.
- [x] Scope required suppressions narrowly and document that Apps remains experimental in SDK 2.0.
- [x] Reference the Apps package with the same `2.*` policy; restore resolves it to `2.0.0`.
- [x] Cover Apps capability, metadata, resource discovery, and serialization through the real MCP endpoint.
- [x] Isolate Apps registration/resources so removal does not alter the Application contract.

### Phase 6 exit criteria

- [x] One tool has an optional interactive view.
- [x] The same tool remains fully useful through text and structured output.
- [x] The App requires no embedded API key.
- [x] CSP and escaping are locally tested; host-specific compatibility remains in live acceptance.
- [x] The experimental feature can be disabled or removed independently.

## Phase 7: Documentation, Client Guidance, And Versioning

### 7.1 Repository conventions

- [x] Update `docs/agent/conventions.md` for annotations and structured outputs.
- [x] Add guidance for Tasks-compatible cancellation and MCP/Application separation.
- [x] Add guidance for schema-versioned result DTOs, stable nullability/collections, and single-source Markdown/structured formatting.
- [x] Add guidance that Apps remain optional presentation adapters.
- [x] Add a one-public-type-per-file structured-result layout example.

### 7.2 User documentation

- [x] Link the shipped MCP 2 capability/operations guide from `README.md`.
- [x] Update `docs/features.md` for MCP 2, structured connection facts, Tasks, and Apps.
- [x] Update `docs/indexing.md` for Tasks, limits, lifecycle-only progress, and separation from REST jobs.
- [x] Record that `execute_context_workflow` is not task-backed, so `docs/context-workflows.md` requires no task instructions.
- [x] Record that neither App calls GraphQL, so `docs/graphql/README.md` requires no browser-auth change.
- [x] Add a compatibility table for VS Code, Codex, Continue, and modern/down-level raw SDK clients, marking live-only rows pending.
- [x] Document the historical `/sse` path as Streamable HTTP, not legacy SSE.

### 7.3 Tool contract documentation

- [x] Document the reviewed read/write/destructive inventory in `docs/mcp-2-capabilities.md`; enforce exact names in integration tests.
- [x] Publish structured output schemas through authenticated `tools/list`, document the versioned shapes, and validate every implemented payload against the advertised schema.
- [x] Document the existing client-extension contract version and text compatibility guarantee.
- [x] Document Tasks opt-in, retention/restart limits, and ordinary-call fallback.
- [x] Document cache hints as advisory.
- [x] Document MCP Apps as experimental, isolated, and disabled by default.

### 7.4 Versioning

- [x] Use feature-specific result versions (`v1` and `find_connection` `1.0`) rather than bumping the indexed graph contract or inventing a universal MCP contract version.
- [x] Treat removal or incompatible change of useful text content as breaking.
- [x] Treat removal or incompatible change of structured properties as breaking within the declared contract version.
- [x] Record package versions, protocol paths, compatibility notes, and rollback controls in `docs/releases/2026-08-mcp-2.md`.
- [x] Retain useful Markdown throughout the 2.x compatibility window; any future removal requires a separately documented breaking release.

## Phase 8: Test Strategy

### 8.1 Unit tests

- [x] Tool annotation classification through discovery contract coverage.
- [x] Structured result construction and wrapper coverage.
- [x] Markdown formatting from typed connection facts, including summary, path, relationship alignment, and empty results.
- [x] Output-schema serialization/discovery.
- [x] Task-store completion, failure, cancellation, duplicate-terminal, late-cancel, TTL, capacity, and oversize transitions.
- [x] Cancellation propagation for task-enabled keyword operations.
- [x] Cache policy through the lower-level list result.
- [x] Optional project-context validation and schema coverage proving no incompatible `x-mcp-header` mirror is advertised.
- [ ] Rate-limit category mapping.
- [x] UI hostile-value sanitization and forbidden DOM sink coverage.

### 8.2 MCP host integration tests

- [x] Connect the SDK 2.0 client through modern server discovery.
- [x] down-level `2025-11-25` initialize fallback.
- [x] authenticated `tools/list`.
- [x] tool annotations on all 62 tools.
- [x] structured output schema discovery and representative typed payload assertions.
- [x] ordinary Tasks fallback and Tasks-disabled fallback.
- [x] task creation, polling, completion, and cancellation.
- [x] cache hints.
- [x] modern/down-level optional project-context calls without a required header mirror.
- [x] Apps resource discovery/read.
- [x] App-disabled behavior.

### 8.3 Architecture tests

- [x] Core has no MCP package dependency.
- [x] Application has no MCP package dependency.
- [x] Infrastructure has no MCP package dependency.
- [x] Cypher remains in Infrastructure.
- [x] MCP protocol response construction remains in McpServer.

### 8.4 Security tests

- [x] Discover, list, call, and Task lifecycle MCP methods reject unauthenticated requests; health/OpenAPI anonymity remains explicit.
- [x] Destructive annotations match the approved two-tool set.
- [ ] Rate limiting cannot be bypassed with spoofed headers.
- [ ] Task ownership is enforced.
- [x] A trace-level capture test proves tool-call logs omit the bearer/API key and raw query/project arguments; the filter never reads document bodies or source snippets.
- [x] UI renders hostile graph-controlled strings as text without creating executable/scriptable nodes.
- [x] App CSP metadata declares empty external domain allowlists.

### 8.5 Performance tests

- [x] Enforce a generous 512 KiB upper bound on the raw `tools/list` response; exact latency/caching-benefit measurement remains a deployment exercise.
- [ ] Measure structured versus markdown result size.
- [ ] Measure task-store overhead.
- [ ] Measure concurrent heavy graph calls.
- [x] Enforce a 64 KiB bound per self-contained UI asset and load both assets in the jsdom harness; real-host load timing remains live work.
- [ ] Establish regression thresholds before enforcing them in CI.

## Verification Commands

Run the narrowest relevant commands first.

### Local verification record (2026-08-04)

- [x] MCP endpoint/host suite: 104 passed, 0 failed, 0 skipped.
- [x] MCP server production build: succeeded with 0 warnings and 0 errors. The full solution build succeeded with 0 errors; recompiling legacy test projects also surfaced their existing nullable-analysis warnings, with none reported in the new MCP 2 files.
- [x] Full .NET solution tests: 980 passed, 0 failed, 0 skipped, including 63 infrastructure integration tests.
- [x] TypeScript/HTML indexer tests: 79 passed across 16 files.
- [x] MCP App DOM/security/accessibility tests: 3 passed.
- [x] TypeScript workspace build: all three workspaces compiled successfully.
- [x] Release publish verification: MCP server published successfully and included both `Apps/client-extension-contract.html` (7,597 bytes) and `Apps/connection-viewer.html` (11,013 bytes).
- [ ] Live deployed-client compatibility remains outstanding and is intentionally separate from local verification.

### MCP host

```powershell
dotnet build src/McpServer/CodeMeridian.McpServer.csproj --no-restore
dotnet test tests/CodeMeridian.McpServer.Tests/CodeMeridian.McpServer.Tests.csproj --no-restore
```

### Application result and job logic

```powershell
dotnet test tests/CodeMeridian.Application.Tests/CodeMeridian.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~KeywordGraphJobService|FullyQualifiedName~CodebaseQueryService"
```

### Infrastructure behavior

```powershell
dotnet test tests/CodeMeridian.Infrastructure.Integration.Tests/CodeMeridian.Infrastructure.Integration.Tests.csproj --no-restore
```

### Full regressions

```powershell
dotnet build CodeMeridian.sln --no-restore
dotnet test CodeMeridian.sln --no-build
npm test
npm run build
```

### Live acceptance

- [ ] Connect with VS Code using `type: "http"`.
- [ ] Connect with Codex using the same Streamable HTTP endpoint.
- [ ] Connect with Continue using `streamable-http`.
- [ ] Capture negotiated protocol version.
- [ ] List all tools and verify annotations.
- [ ] Call every structured-output pilot.
- [ ] Start, poll, and cancel a task.
- [ ] Verify cache hints with a raw/SDK client.
- [ ] Open the MCP App in every claimed compatible host.
- [ ] Confirm old clients retain useful ordinary text results.

## Rollout Strategy

### Release slice 1: safety metadata

- [ ] Ship Phase 1 independently.
- [ ] Confirm no tool names or behavior changed.
- [ ] Monitor client approval behavior.
- [ ] Roll back individual annotations if a client interprets them incorrectly.

### Release slice 2: structured-output pilots

- [ ] Ship no more than two pilots initially.
- [ ] Keep text compatibility.
- [ ] Collect client behavior and payload metrics.
- [ ] Expand only after the compatibility gate passes.

### Release slice 3: headers and caching

- [ ] Ship telemetry before enforcement.
- [ ] Observe header availability and cardinality.
- [ ] Enable rate limits after measurements.
- [ ] Add caching hints after visibility rules are settled.

### Release slice 4: Tasks

- [x] Make Tasks independently disableable with `Mcp:Tasks:Enabled` (enabled by default after local verification).
- [x] Start with keyword maintenance only.
- [x] Document restart/durability semantics.
- [x] Roll back with `Mcp__Tasks__Enabled=false` while retaining ordinary tool calls.

### Release slice 5: MCP App

- [x] Implement as experimental and disabled by default; release publication remains separate.
- [x] Limit the implementation to two bounded read-only views backed by existing structured tools.
- [x] Do not block normal tool use on UI support.
- [x] Keep removal isolated from the core tool/Application contract.

## Observability And Acceptance Signals

Track:

- tool calls by approved bounded category
- duration and cancellation rate
- protocol error rate
- structured output payload size
- ordinary versus task-backed calls
- task completion, failure, cancellation, and expiration
- discovery payload size and refresh frequency
- app resource reads and render failures

Do not track:

- API keys
- document contents
- query/source text
- full file paths as metric labels
- arbitrary project names as unbounded metric labels
- raw task results

## Risks And Mitigations

### Client capability fragmentation

Risk:

- not every client will immediately consume structured content, Tasks, cache hints, or Apps

Mitigation:

- [x] preserve ordinary text and tool calls
- [x] gate Apps and make Tasks independently disableable
- [ ] publish a tested compatibility matrix

### Contract duplication

Risk:

- typed facts and markdown can diverge

Mitigation:

- [x] construct both from one typed result model
- [x] add parity assertions for key values
- [x] avoid Markdown-to-DTO parsing

### Application refactor expansion

Risk:

- changing string-returning services could spread across many tests and callers

Mitigation:

- [x] pilot one bounded vertical slice
- [x] use CodeMeridian impact/test-shield tools before conversion
- [x] avoid a universal abstraction until repetition is proven

### Task durability and ownership

Risk:

- stateless requests may hit different processes or server restarts may lose task state

Mitigation:

- [x] use a singleton store for one-process deployments
- [x] document that a durable shared store is required before multi-replica claims
- [ ] bind tasks to authenticated principals

### Experimental Apps churn

Risk:

- `MCPEXP003` APIs may change

Mitigation:

- [x] isolate Apps code
- [x] feature-flag registration
- [x] keep tool contracts UI-independent

### Header spoofing

Risk:

- clients can send header values directly

Mitigation:

- [x] treat standardized parameter headers as optional metadata only
- [x] derive tool telemetry from the parsed matched primitive, not spoofable headers
- [x] retain endpoint authentication

### Cache staleness

Risk:

- clients can retain tool definitions after feature or policy changes

Mitigation:

- [x] conservative five-minute TTL
- [x] private scope for authenticated discovery
- [x] deployment/restart plus bounded TTL as the initial invalidation policy

## Open Questions

- [x] Declare annotations explicitly on each attribute and enforce the reviewed inventory in one endpoint contract test.
- [x] Do not introduce common structured metadata until repetition proves it useful.
- [x] Use the three existing typed client-extension tools for the first slice, then add only the bounded `find_connection` graph pilot.
- [x] Keep `execute_context_workflow` read-only/non-destructive because Application refuses mutating steps.
- [x] Use `WithTasks` with an execution-mode selector limited to keyword maintenance.
- [x] Accept and document in-memory task loss on restart for the first single-process release.
- [ ] Should REST keyword jobs eventually support cancellation?
- [x] Remove the `Mcp-Param-ProjectContext` experiment after it proved incompatible with an optional argument; require new client evidence before another pilot.
- [x] Use a private five-minute `tools/list` TTL.
- [ ] Which clients currently render MCP Apps?
- [x] Render only tool-provided data in the first App; do not expose GraphQL auth to the iframe.
- [x] Reuse the existing `v1` client-extension contract and use a feature-specific `1.0` connection contract; do not introduce an MCP-wide result hierarchy.

## Remaining Work Order

1. Complete the broad local regression and publish-output verification recorded below.
2. Deploy the finished local batch once, without an intermediate graph re-index.
3. Exercise VS Code, Codex, and Continue against the same server build.
4. Run ordinary and task-backed keyword maintenance against real graph data, including cancellation.
5. Open both Apps in every host that will be claimed as compatible and capture evidence.
6. Decide on any further structured tools, rate limits, durable Tasks, or parameter-header work only from measured evidence.

## Overall Success Criteria

- [x] All 62 tools advertise reviewed and correct behavioral annotations.
- [x] Four bounded tools, including `find_connection`, provide advertised schema-valid structured facts and compatible Markdown.
- [ ] Supported clients can continue calling every existing tool.
- [x] Keyword maintenance can run as a cancellable MCP Task when the client opts in.
- [x] Discovery responses include an approved safe TTL and cache scope.
- [x] Parsed-tool telemetry remains outside the authorization boundary, and the incompatible optional standardized-header mirror is not shipped.
- [x] Two optional read-only MCP Apps prove safe typed-data rendering, including an interactive connection path.
- [x] No MCP SDK dependency leaks into Core or Application.
- [x] Focused, full .NET, TypeScript, App security, and local protocol compatibility checks pass.
- [ ] Live VS Code, Codex, Continue, real-graph Task, and host App acceptance checks pass.
- [x] Documentation accurately describes implemented behavior and experimental/operational limitations.

## Definition Of Done

- [ ] Decision gates are resolved and recorded.
- [x] Tool inventory and annotation tests are permanent.
- [x] Structured output contracts are versioned and validated against advertised schemas.
- [ ] Task persistence, ownership, cancellation, timeout, and cleanup behavior are documented and tested.
- [x] Cache/header policies avoid shared discovery and do not use mirrored headers for authorization.
- [x] Apps are isolated, optional, CSP-constrained, and endpoint-tested.
- [x] All locally relevant docs and client examples are updated; host-specific claims remain pending live acceptance.
- [x] Release notes include protocol, SDK, compatibility, and rollback details.
- [ ] Live acceptance succeeds against the deployed MCP server.

## Reference Material

- [C# SDK 2.0 release notes](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.0.0)
- [C# SDK tools documentation](https://csharp.sdk.modelcontextprotocol.io/concepts/tools/tools.html)
- [C# SDK Tasks documentation](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/tasks/tasks.html)
- [MCP Apps builder API](https://csharp.sdk.modelcontextprotocol.io/v2/api/ModelContextProtocol.Extensions.Apps.McpAppsBuilderExtensions.html)
- [MCP App UI attribute API](https://csharp.sdk.modelcontextprotocol.io/v2/api/ModelContextProtocol.Extensions.Apps.McpAppUiAttribute.html)
- [ListToolsResult caching hints](https://csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.Protocol.ListToolsResult.html)
- [MCP request filters](https://csharp.sdk.modelcontextprotocol.io/concepts/filters.html)
- [VS Code MCP configuration](https://code.visualstudio.com/docs/agents/reference/mcp-configuration)
