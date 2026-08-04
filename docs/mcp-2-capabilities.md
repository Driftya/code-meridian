# MCP 2 Capabilities

CodeMeridian uses the stable 2.x C# MCP SDK over stateless Streamable HTTP. The configured route remains `/sse` for compatibility with existing client files; the route name does not mean the removed stateful SSE transport is in use.

This page describes the implemented MCP 2 contract, compatibility behavior, operational limits, and intentionally deferred deployment work. The detailed checklist is in [the MCP 2 capability adoption plan](plans/2026-08-04-mcp-2-capability-adoption-plan.md).

Package, protocol, compatibility, and rollback changes are summarized in [the local MCP 2 adoption release notes](releases/2026-08-mcp-2.md).

## Compatibility Status

| Client path | Locally verified | Notes |
|---|---:|---|
| C# SDK default negotiation | Yes | Negotiates `2026-07-28` through `server/discover`; stateless raw-wire baseline is checked in. |
| C# SDK pinned down-level | Yes | Negotiates `2025-11-25` through `initialize`; tools, annotations, structured results, Tasks, cache hints, and text fallbacks remain available. |
| Non-App MCP clients | Yes | Apps are disabled by default, and App-backed tools retain ordinary text and structured results. |
| VS Code | Pending live acceptance | Generated configuration uses HTTP against the existing `/sse` path. |
| Codex | Pending live acceptance | Must be exercised against the deployed server before claiming host-specific optional-capability support. |
| Continue | Pending live acceptance | Must be exercised against the deployed server before claiming host-specific optional-capability support. |

The local modern and down-level baselines intentionally lock stable contract facts rather than entire response bodies: negotiated version, handshake path, tool count, annotation count, structured-tool inventory, capability flags, and discovery cache policy. Descriptions remain outside strict snapshots so documentation improvements do not cause protocol churn.

## Tool Safety Metadata

All 62 registered tools advertise an explicit title and reviewed `readOnlyHint`, `destructiveHint`, `idempotentHint`, and `openWorldHint` values.

- 54 tools are read-only.
- Eight tools can mutate CodeMeridian-controlled state.
- `clear_project_knowledge` and `clear_code_graph` are the two destructive tools.
- All tools use `openWorldHint: false`; none contacts an unpredictable external service.
- Mutating tools use conservative idempotency hints unless their repeated effect is known to be safe.

`execute_context_workflow` remains read-only. Its Application implementation refuses mutating and destructive workflow steps even when the legacy `allowGraphMutation` argument is supplied.

Annotations are client hints. Authentication, validation, and server-side policy remain the security boundary.

## Structured Results

Four tools advertise an `outputSchema`, return camel-case `structuredContent`, and preserve useful Markdown in a text content block:

- `find_connection`
- `get_client_extension_contract`
- `list_client_extension_examples`
- `get_client_extension_example`

The three client-extension tools retain their existing `v1` contract. `find_connection` uses `contractVersion: "1.0"` and returns:

| Property | Contract |
|---|---|
| `fromId`, `toId` | Requested exact graph node IDs. |
| `maxHops`, `pathFound`, `hopCount`, `truncated` | Bounded path state. `truncated` is currently always `false`. |
| `nodes` | Ordered, non-null array of ID, name, type, namespace, file, line, and project facts. |
| `edges` | Ordered, non-null array of source, target, and relationship facts. |
| `frontendSignals` | Non-null array of bounded frontend relationship summaries. |

The connection payload deliberately excludes source snippets and arbitrary graph properties. Empty results use stable empty arrays rather than `null`. Declared nullable properties are serialized even when `null` so actual payloads continue matching the advertised schema.

Both structured data and Markdown are produced from the same protocol-neutral Application record. MCP response construction and SDK attributes stay in `McpServer`; no MCP package dependency enters `Application` or `Core`.

Endpoint tests validate every implemented structured response against its advertised JSON Schema, reject an invalid connection shape, enforce a 128 KiB pilot payload ceiling, and exercise the down-level text fallback.

The schema returned by authenticated `tools/list` is the authoritative machine-readable contract. Any removal or incompatible property/type change is breaking within its declared result version; additive nullable fields require compatibility review.

Useful Markdown remains part of the CodeMeridian 2.x compatibility contract. Removing it requires a separately documented breaking release and live-client migration evidence.

## Standard Parameter Header Decision

No tool currently advertises `[McpHeader]` parameter mappings.

An initial `query_codebase.projectContext` pilot was removed after endpoint testing showed the SDK treats `Mcp-Param-*` as an integrity mirror of the JSON argument, not as an alternate argument source. Applying it to an optional parameter made ordinary modern calls without the mirror fail. The JSON `projectContext` argument therefore remains canonical for every client.

The host still validates `query_codebase.projectContext` before Application code runs: the value is limited to 200 characters and rejects control characters. Project context is not used as an authorization signal or an unbounded metric label. A future header pilot requires client evidence and must use a parameter whose required/optional semantics are compatible with the SDK mirror contract.

## Discovery Caching

Modern and tested down-level clients receive these authenticated `tools/list` hints:

- TTL: five minutes
- scope: `private`

`private` prevents a shared intermediary from reusing an authenticated catalog across principals. The hints are advisory and do not cache graph query results. Deployment/restart is the catalog invalidation boundary, while the five-minute TTL bounds stale discovery after feature configuration changes.

Unauthenticated MCP discovery and call requests receive `401` before cacheable contract data is returned.

## Tool Telemetry

The MCP call filter records bounded operational metadata only:

- structured log fields: registered tool name, elapsed milliseconds, and error state/outcome
- activity source: `CodeMeridian.McpServer`
- activity name: `mcp.tools.call`
- activity tags: `mcp.tool.name`, `mcp.tool.category`, and `mcp.tool.outcome`
- counter: `codemeridian.mcp.tool.calls`
- histogram: `codemeridian.mcp.tool.duration`, in milliseconds

Metric categories are bounded to `query`, `mutation`, `maintenance`, `destructive`, and `unknown`. Outcomes are bounded to `success`, `tool_error`, `cancelled`, `failure`, `timeout`, and `result_too_large`.

The filter does not log raw arguments, project names, API keys, bearer tokens, document bodies, query text, source snippets, or task results.

## MCP Tasks

`rebuild_keyword_graph` and `classify_keywords` support client-opted MCP Tasks. Every other tool remains synchronous.

A normal `tools/call` still returns the ordinary result. A Tasks-capable client may opt into a task, poll it, and cancel it. `tasks/cancel` propagates through `IKeywordGraphService` into the underlying Neo4j operation.

Tasks are enabled by default and can be removed from capability discovery without disabling ordinary tools:

```text
Mcp__Tasks__Enabled=false
```

Runtime settings and defaults:

| Setting | Default | Enforced bound or behavior |
|---|---:|---|
| `Mcp:Tasks:PollIntervalMilliseconds` | `500` | Clamped to 100–60,000 ms. |
| `Mcp:Tasks:TimeToLiveMinutes` | `30` | Clamped to 1–1,440 minutes. |
| `Mcp:Tasks:MaxActiveTasks` | `4` | Global process capacity, clamped to 1–100. |
| `Mcp:Tasks:MaxDurationSeconds` | `1800` | Maintenance execution timeout, clamped to 1–86,400 seconds. |
| `Mcp:Tasks:MaxResultBytes` | `131072` | Serialized result ceiling, clamped to 1 KiB–4 MiB. |

Operational behavior:

- The store is a process-local singleton; task state is lost on restart and is unsuitable for multi-replica routing.
- Completed, failed, cancelled, and expired tasks release their active reservation once. Duplicate terminal updates and late cancellation cannot overwrite the first terminal result.
- Expected tool/domain failures complete as a `CallToolResult` with `isError: true`; protocol/store failures use the protocol `failed` state.
- Oversized or timed-out maintenance results complete with a bounded tool error rather than leaking an unbounded body or exception detail.
- The health system reports process-local storage, active/global capacity, TTL, and result-size policy through the `mcp_tasks` check.
- Metrics expose `codemeridian.mcp.task.active`, `codemeridian.mcp.task.duration`, and `codemeridian.mcp.task.rejections` with bounded outcome/reason tags.
- REST keyword jobs and MCP task IDs remain separate. The REST background path still has its own lifecycle and does not inherit MCP cancellation.

If a task appears stuck, poll `tasks/get`, request `tasks/cancel`, inspect the `mcp_tasks` health check and task duration/rejection metrics, and verify the underlying Neo4j operation. Do not raise duration or capacity limits until the cause is understood. A process restart discards the process-local task record; use `Mcp__Tasks__Enabled=false` to remove the task surface while preserving ordinary calls.

CodeMeridian currently authenticates one configured API-key identity. It therefore does not claim multi-principal task ownership. A durable principal-aware store is required before restart survival, multiple replicas, or identity isolation can be advertised. Phase/percentage progress and an explicit host-shutdown drain policy also remain deferred.

## Experimental MCP Apps

The Apps package remains experimental in the 2.0 SDK, so registration is disabled by default:

```text
Mcp__Apps__Enabled=false
```

When enabled, two read-only views are registered:

| Tool | Resource |
|---|---|
| `get_client_extension_contract` | `ui://code-meridian/client-extension-contract` |
| `find_connection` | `ui://code-meridian/connection-viewer` |

Both views render the tool's typed `structuredContent`, remain self-contained, and declare empty external connection, resource, frame, and base-URI allowlists. They contain no server API key, authorization header, GraphQL credentials, destructive controls, or external assets.

Both tools explicitly advertise `model` and `app` visibility: the model may invoke them normally, and the associated read-only App may refresh the same factual result. Neither is an app-only hidden mutation surface.

The connection viewer renders ordered paths, nodes, relationship types, source locations, frontend signals, and explicit empty/truncated states. Graph-controlled strings enter the DOM through `textContent`; the app does not use `innerHTML`, `insertAdjacentHTML`, `document.write`, `eval`, or dynamic function construction.

A jsdom security/accessibility harness injects hostile HTML-shaped values, proves they remain text, checks that no script or image nodes are created, scans forbidden DOM sinks and external assets, and verifies semantic headings, lists, status announcements, labels, and keyboard-operable controls.

Non-App clients are unaffected. Both tools remain fully useful through Markdown and structured content, and disabling Apps removes only capability/resource metadata.

## Authentication And Statelessness

Authentication applies to `server/discover`, `initialize`, `tools/list`, `tools/call`, and the Tasks methods. Tests cover unauthenticated discover/list/call/get/update/cancel requests and confirm `401` responses.

The modern raw-wire baseline confirms stateless discovery/calls do not issue `Mcp-Session-Id`, expose the API key, or require a legacy GET event stream. The pinned `2025-11-25` compatibility path also works through ordinary HTTP initialization and tool calls.

## Rollback

Capabilities degrade independently:

- set `Mcp__Apps__Enabled=false` to remove experimental Apps
- set `Mcp__Tasks__Enabled=false` to remove Tasks while preserving ordinary calls
- keep structured-result text blocks for older clients
- treat cache hints as advisory
- retain the existing HTTP `/sse` URL and authentication settings

The SDK upgrade, stateless transport, safety annotations, and versioned result contracts do not require a graph re-index.

## Remaining Adoption Work

The following work is intentionally not claimed as complete:

- run the final VS Code, Codex, and Continue matrix against the updated deployed server
- capture host-specific screenshots and confirm which hosts render MCP Apps
- exercise the deployed keyword Tasks against real graph data, including rebuild/classify duration and cancellation integrity
- add principal-aware authorization if more than one authenticated identity is introduced
- select durable/shared task storage before any multi-replica deployment
- define phase/percentage progress and host-shutdown drain/cancel semantics
- measure production tool latency and concurrency before adding tool-aware rate limits
- decide from client evidence whether additional graph tools justify structured conversion or another App
- publish release notes and perform the deployment/restart acceptance batch

No live server update or graph index rebuild is required for the local implementation and test work above.

## Local Verification

Permanent coverage includes:

- exact 62-tool inventory, titles, and behavior annotations
- object input schemas and the absence of an incompatible optional parameter-header mirror
- modern and pinned `2025-11-25` raw-wire contract baselines
- schema-valid structured content, invalid-schema rejection, payload bounds, empty collections, and text fallback
- private five-minute discovery caching hints
- bounded tool activities and metrics
- ordinary-call, task-backed, disabled, completion, error, timeout, oversize, TTL, capacity, cancellation, health, and task-metric behavior
- Apps-disabled behavior plus capability, metadata, resources, CSP, injection, and accessibility checks when enabled
