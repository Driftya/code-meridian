# Client GraphQL Extension MCP Tool Plan

- Status: proposed
- Date: 2026-07-10
- Scope: remove the dead server-side sub-agent abstraction and replace the current project-agent direction with a client-extension model built on the existing read-only GraphQL surface plus a thin MCP contract
- Assumption: "client can have their own behaviours" means CodeMeridian should stop pretending to orchestrate external agents server-side and should instead expose a stable contract that client-side extensions can use to implement their own routing, prompts, UI, and query behavior
- Principle: keep CodeMeridian responsible for facts, schema, auth, and bounded graph access; keep client-specific behavior on the client

## Problem Statement

The current sub-agent shape is not earning its keep:

- `src/Core/Agents/ISubAgent.cs` defines a server-side abstraction that is not on a live execution path
- `src/Application/SubAgents/CodeGraphSubAgent.cs` and `src/Application/SubAgents/ExtensionProxySubAgent.cs` implement it, but nothing resolves or invokes them
- current MCP extension tools in `src/McpServer/Tools/ExtensionTools.cs` still assume CodeMeridian should register and call external HTTP "project agents"
- that model conflicts with the requested direction where clients own their own behaviors

At the same time, the repo already has a better foundation for client-owned behavior:

- a live read-only `/graphql` endpoint in the same ASP.NET host
- bounded generic graph query services underneath it
- GraphQL docs under `docs/graphql/`

The missing piece is a coherent MCP-facing extension contract that tells clients how to behave against that GraphQL surface without reviving dead sub-agent code or inventing a server-side multi-agent runtime.

## Verified Current State

- `src/Core/Agents/ISubAgent.cs` describes sub-agents as discovered by an orchestrator and invoked in parallel, but there is no composition-root registration or orchestrator using it.
- `src/Application/SubAgents/CodeGraphSubAgent.cs` is an unused in-process graph-answering adapter.
- `src/Application/SubAgents/ExtensionProxySubAgent.cs` is an unused HTTP proxy adapter for external agents.
- `src/Application/SubAgents/VectorSearchSubAgent.cs` is only a placeholder comment.
- `src/McpServer/Tools/ExtensionTools.cs` exposes:
  - `register_project_agent`
  - `unregister_project_agent`
  - `list_project_agents`
  - `call_project_agent`
- those tools manage external HTTP endpoints rather than client-side extension behavior
- `src/Application/Services/ContextWorkflows/ContextWorkflowToolCatalog.cs` and `ContextWorkflowPlanner.cs` still advertise those project-agent tools as a supported workflow
- `src/McpServer/Program.cs` already hosts both MCP and GraphQL in one process
- `docs/plans/2026-07-10-hotchocolate-graphql-doc-query-plan.md` already establishes GraphQL as the repo's bounded read surface

## Goal

Replace the dead and misleading "sub-agent" direction with a client-extension model where:

1. CodeMeridian no longer contains unused server-side sub-agent code
2. MCP no longer presents external project agents as the preferred extension model
3. clients can discover a stable extension contract from MCP
4. client-side extensions can use CodeMeridian's authenticated GraphQL endpoint to implement their own behaviors
5. the server remains deterministic, bounded, and fact-oriented

## Non-Goals

- Do not build a server-side multi-agent orchestrator.
- Do not execute arbitrary client code inside CodeMeridian.
- Do not add write-capable GraphQL mutations for extension behavior in v1.
- Do not turn MCP into a generic remote plugin host.
- Do not remove the existing GraphQL endpoint.

## Chosen Direction

Delete the dead sub-agent code and replace the current "project agent" story with a "client extension contract" story.

That contract should have two layers:

- GraphQL remains the real data surface that client extensions use for graph reads
- a small MCP tool surface provides discovery, documentation, capability metadata, examples, and guardrails for clients that want to build their own behaviors on top of GraphQL

This keeps behavior where it belongs:

- CodeMeridian owns graph facts and transport contracts
- clients own prompts, routing, UI, query composition, and behavior chaining

## Why This Direction Fits The Repo

- The repo already has a bounded generic graph read seam and a live `/graphql` endpoint.
- The current sub-agent code is dead, so extending it would create more architecture than value.
- GraphQL is a better fit for client-specific behavior than a server-owned callback contract.
- MCP is still useful here as a discoverable contract surface for LLM clients.
- This avoids maintaining two competing extension models:
  - server-calls-external-agent
  - client-calls-graphql

## Proposed User-Facing Model

The new model should read like this:

- CodeMeridian exposes graph knowledge through GraphQL and MCP
- client extensions can implement their own behaviors by:
  - discovering the available extension contract from MCP
  - reading schema and usage details
  - issuing bounded GraphQL queries
  - composing the returned facts however they want on the client

CodeMeridian should stop advertising:

- "register a project agent so CodeMeridian can call it"

CodeMeridian should instead advertise:

- "discover the GraphQL extension contract and build your own client-side behavior against it"

## Proposed MCP Surface

Replace the project-agent MCP surface with a small extension-contract surface.

### Recommended first tool set

- `get_client_extension_contract`
- `list_client_extension_examples`
- `get_client_extension_example`

### `get_client_extension_contract`

Purpose:

- return the canonical contract a client extension should use

Suggested response content:

- GraphQL endpoint path
- auth requirements
- supported transport headers
- page-size, depth, and complexity limits
- supported sort fields
- a short description of the generic graph schema
- links or inline references to example GraphQL documents under `docs/graphql/`
- explicit statement that client behavior is client-owned, not server-hosted

### `list_client_extension_examples`

Purpose:

- list available example behaviors and example GraphQL documents

Suggested response content:

- example name
- short description
- related `.graphql` document path
- intended use case

### `get_client_extension_example`

Purpose:

- return one example extension recipe in a bounded, deterministic form

Suggested response content:

- example name
- goal
- GraphQL document
- variables template
- expected result shape
- notes about limits and auth

This keeps MCP deterministic while still being genuinely useful to a client that wants to implement custom behavior.

## What To Remove

### Dead code

- `src/Core/Agents/ISubAgent.cs`
- `src/Core/Agents/IAgent.cs`
- `src/Core/Agents/AgentContext.cs`
- `src/Core/Agents/AgentRequest.cs`
- `src/Core/Agents/AgentResponse.cs`
- `src/Application/SubAgents/CodeGraphSubAgent.cs`
- `src/Application/SubAgents/ExtensionProxySubAgent.cs`
- `src/Application/SubAgents/VectorSearchSubAgent.cs`

Remove only after verifying there are no remaining production references.

### Old extension-agent workflow surfaces

Remove or replace:

- `register_project_agent`
- `unregister_project_agent`
- `list_project_agents`
- `call_project_agent`

Also update:

- `src/Application/Services/ContextWorkflows/ContextWorkflowToolCatalog.cs`
- `src/Application/Services/ContextWorkflows/ContextWorkflowPlanner.cs`
- `docs/features.md`
- `docs/context-workflows.md`

## Architecture Plan

### 1. Remove the dead server-side sub-agent abstraction

- verify there are no production or test references outside the dead files
- delete the dead interfaces and implementations
- remove any package or using statements that only existed for that surface

### 2. Define a client-extension contract model in Core or Application

Add a small read-only contract that can be reused by MCP tools.

Suggested shape:

- `ClientExtensionContract`
- `ClientExtensionExample`

Suggested fields:

- contract version
- GraphQL endpoint
- auth header names
- limits
- supported sort fields
- example ids
- notes

Keep this as metadata, not executable behavior.

### 3. Add an Application service for extension contract discovery

Suggested shape:

- `IClientExtensionService`
- `ClientExtensionService`

Responsibilities:

- provide the current extension contract
- enumerate example behaviors
- load one example by id
- centralize the canonical docs/limits/messages so MCP tools and docs stay aligned

### 4. Replace `ExtensionTools` with a contract/discovery tool surface

Possible shape:

- keep the file name if that causes less churn, but rename the public story from "project agents" to "client extensions"
- or rename the tool class to `ClientExtensionTools`

Responsibilities:

- expose the new discovery tools
- return deterministic markdown or DTO-backed text
- never call arbitrary external endpoints

### 5. Reuse the existing GraphQL documentation folder

Use `docs/graphql/` as the canonical example source where possible.

The tool layer can:

- list curated example ids
- map each id to a checked-in `.graphql` file
- return the example query and short explanation

This keeps examples versioned with the repo instead of hardcoding large query strings in attributes or tool methods.

### 6. Update workflow planning and docs

Replace the current extension-agent workflow recipe with a client-extension discovery recipe.

The new workflow should help the model do things like:

- discover how to build a custom client behavior
- find GraphQL examples
- understand auth and limits

It should no longer suggest registering remote agents.

## Suggested First Implementation Slice

Keep the first slice intentionally small:

- remove the dead `ISubAgent` and related sub-agent files
- replace the old project-agent MCP tools with a read-only discovery surface
- return the GraphQL endpoint, auth requirements, and usage limits from MCP
- expose a curated list of example GraphQL documents from `docs/graphql/`
- update workflow planning and docs to describe client-owned behavior correctly

Do not add:

- extension manifests stored in the graph
- client profile persistence
- server-side behavior execution
- dynamic template authoring tools

## Test Plan

### Unit and application tests

- [ ] deleting the sub-agent files does not break compilation or service registration
- [ ] `ClientExtensionService` returns the expected GraphQL endpoint metadata
- [ ] `ClientExtensionService` returns the expected auth header names
- [ ] `ClientExtensionService` returns the configured limits and sort fields that match the live GraphQL docs
- [ ] `ClientExtensionService` lists available curated examples deterministically
- [ ] requesting an unknown example id fails cleanly

### MCP tool tests

- [ ] `get_client_extension_contract` returns the expected endpoint, auth, and limits
- [ ] `list_client_extension_examples` returns the expected checked-in examples
- [ ] `get_client_extension_example` returns the expected query text and metadata
- [ ] MCP responses stay bounded and deterministic

### Regression tests

- [ ] GraphQL endpoint behavior remains unchanged
- [ ] existing graph-query MCP tools remain unchanged
- [ ] removing project-agent tools does not leave stale entries in workflow planning
- [ ] docs and tool descriptions no longer mention project-agent registration as the extension path

## Documentation Plan

- [ ] Update `docs/features.md` to remove project-agent wording and describe client extensions based on GraphQL.
- [ ] Update `docs/context-workflows.md` to replace `extension_agent_routing` with a client-extension discovery workflow.
- [ ] Update `docs/graphql/README.md` with a short "building client extensions" section.
- [ ] Add or update one focused doc that explains:
  - what a client extension is
  - what MCP provides
  - what GraphQL provides
  - what remains client-owned

## Implementation Phases

### Phase 0. Confirm naming and contract

- [ ] Decide whether the user-facing term is `client extension`, `GraphQL extension`, or `custom client behavior`.
- [ ] Decide whether the new MCP surface replaces `ExtensionTools` in place or lands as a new tool class with the old tools removed.
- [ ] Decide whether example queries are embedded in code, loaded from files, or both.

### Phase 1. Remove dead sub-agent code

- [ ] verify reference count is zero outside the dead files
- [ ] delete the dead `Core/Agents` contracts
- [ ] delete the dead `Application/SubAgents` files
- [ ] remove any stale usings or package dependencies made unnecessary by the deletion

### Phase 2. Add extension contract service

- [ ] add `ClientExtensionContract` and `ClientExtensionExample` DTOs
- [ ] add `IClientExtensionService`
- [ ] implement `ClientExtensionService`
- [ ] source contract values from the live server configuration and checked-in docs where practical

### Phase 3. Add MCP discovery tools

- [ ] replace project-agent registration/call tools with read-only client-extension discovery tools
- [ ] keep tool descriptions precise about client-owned behavior
- [ ] ensure tool output points to the real `/graphql` surface and checked-in examples

### Phase 4. Update workflow planning and docs

- [ ] replace `ExtensionAgents` entries in `ContextWorkflowToolCatalog`
- [ ] replace the `extension_agent_routing` recipe in `ContextWorkflowPlanner`
- [ ] update repo docs and examples

### Phase 5. Validate end to end

- [ ] run targeted tests for the new service and MCP tools
- [ ] verify the GraphQL endpoint and auth examples still work
- [ ] verify there are no stale `sub-agent` or `project agent` references except where historical docs intentionally remain

## Open Questions

- Should the MCP tool return file-backed GraphQL examples inline, or only return paths plus summaries?
- Should the extension contract expose the absolute runtime URL, or only the route path such as `/graphql`?
- Should client-extension docs live under `docs/graphql/` only, or get a dedicated `docs/extensions/` folder?
- Do we want one example that explicitly shows how a client can compose multiple GraphQL queries into a higher-level behavior?

## Success Criteria

- [ ] there is no dead `ISubAgent` or `Application/SubAgents` code left in production
- [ ] MCP no longer advertises server-called project agents as the extension model
- [ ] a client can discover how to build its own behavior against CodeMeridian through a small MCP tool surface
- [ ] the discovered contract points to the real GraphQL endpoint, auth rules, limits, and example queries
- [ ] tests cover the new discovery surface and protect against doc/contract drift

## Definition Of Done

- [ ] dead sub-agent code is removed
- [ ] project-agent MCP tools are removed or replaced
- [ ] new client-extension MCP discovery tools are live
- [ ] workflow planning and docs align with the new client-owned behavior model
- [ ] regression tests pass for the new slice
