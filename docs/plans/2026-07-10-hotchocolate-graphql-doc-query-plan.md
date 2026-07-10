# Hot Chocolate GraphQL Neo4j Graph Query Plan

- Status: partially implemented
- Date: 2026-07-10
- Scope: expose the existing Neo4j graph through a read-only Hot Chocolate GraphQL surface, then make that same capability reachable through both HTTP API and MCP so an LLM can query existing labels, nodes, properties, relationships, and keyword graph data
- Assumption: the request said `mpc`; this plan assumes `MCP`
- Principle: expose the real existing graph generically enough to query any current labels and relationships, but keep the surface read-only, bounded, and deterministic
- Delivery note: the first HTTP GraphQL foundation landed on 2026-07-10 with dedicated `GraphQueries` namespaces in Core, Application, Infrastructure, and `src/McpServer/GraphQl/`; MCP adapter work remains open by design

## Problem Statement

The actual goal is broader:

- allow an LLM to query any existing nodes in the Neo4j database
- expose existing labels, properties, and relationships
- include keyword graph data in that queryable surface
- make that graph queryable through:
  - HTTP API
  - MCP
- use Hot Chocolate as the GraphQL layer

In this repo, that means the new surface must work across the graph that CodeMeridian already persists, including:

- `CodeNode`
- `KnowledgeDocument`
- keyword graph nodes such as extracted keywords and keyword source nodes
- `ConfigurationEntry`
- `ConfigurationFile`
- `ApiEndpoint`
- `DatabaseTable`
- `MessageTopic`
- `ExternalConcept`
- existing relationship types such as `Calls`, `DependsOn`, `Uses`, `Contains`, `Mentions`, `References`, `Reads`, `Writes`, `PublishesTo`, and `SubscribesTo`
- existing keyword graph relationships such as keyword-to-source and keyword-to-related-node edges

It should not mean:

- write-capable GraphQL mutations in the first slice
- unrestricted raw Cypher execution
- a schema that only models documents
- bypassing the existing Application/Core seams

## Verified Current State

- `src/McpServer/Program.cs` already hosts the API and MCP server in one ASP.NET process.
- `src/Infrastructure/Graph/Neo4jCodeGraphRepository.cs` and its partials already query and traverse `CodeNode` graph data.
- `src/Infrastructure/Knowledge/Neo4jVectorRepository.cs` already reads and writes `KnowledgeDocument` nodes plus `Mentions` and `References` relationships.
- `src/Core/CodeGraph/ICodeGraphRepository.cs` is the current abstraction for code-node graph queries.
- `src/Core/Knowledge/IVectorRepository.cs` is the current abstraction for knowledge-document reads.
- `src/Core/KeywordGraph/` already defines keyword graph concepts that should remain queryable through the same generic surface.
- Current persisted graph data is not one single domain model. It includes:
  - code graph nodes under the `CodeNode` label with a `type` discriminator
  - knowledge documents under the `KnowledgeDocument` label
  - keyword graph data for extracted keywords, source nodes, and related-node links
  - configuration graph data such as `ConfigurationEntry`
  - external concept and tracing data already projected into the graph
- There is no existing Hot Chocolate or GraphQL package in the repo today.

## Goal

Add a read-only GraphQL capability that lets clients query the existing Neo4j graph generically enough to inspect:

1. node labels
2. node properties
3. relationship types
4. relationship endpoints
5. basic neighborhood traversal
6. bounded filtering by label, property, project, and relationship type
7. keyword graph nodes and relationships through the same generic contract

The same underlying query seam must be reusable from:

- `/graphql`
- MCP tools

## Non-Goals

- Do not add write mutations in v1.
- Do not expose a raw Cypher execution endpoint to MCP or HTTP clients in v1.
- Do not re-model every existing Neo4j structure into rigid handwritten GraphQL object types before shipping any value.
- Do not replace existing REST or MCP tools.
- Do not move graph-query logic into GraphQL resolvers.
- Do not promise that the server answers natural-language questions directly; the LLM still maps questions to structured queries.

## Chosen Direction

Build a generic graph-query layer in Application that can read existing Neo4j labels, nodes, properties, and relationships in a bounded way, then expose it through:

- Hot Chocolate GraphQL on `/graphql`
- one or more MCP tools that forward structured graph queries to the same Application seam

The key design choice is:

- GraphQL should expose the current graph as queryable data
- but the actual graph access rules, filters, limits, and allowed traversal behavior should live in Application and Infrastructure, not inside the GraphQL adapter

## Why This Direction Fits The Repo

- The repo already has a single ASP.NET host for API plus MCP, so GraphQL is an additive presentation surface.
- The existing graph is already broad and not doc-only.
- Existing CodeMeridian behavior is fact-returning rather than reasoning-heavy; a generic read-only graph schema can keep that contract intact.
- Existing repositories already separate code-graph and knowledge-document reads, so the plan can extend those seams instead of inventing a parallel stack.
- A generic read layer is the cleanest place to make keyword graph data queryable without adding keyword-specific transport logic first.

## Proposed Query Model

The first slice should expose the existing graph in a generic, introspectable shape rather than a document-specific shape.

### Root queries

- `labels`
- `relationshipTypes`
- `nodes(filter, limit, skip)`
- `node(id)`
- `relationships(filter, limit, skip)`
- `neighbors(nodeId, relationshipTypes, direction, limit, depth)`
- `schemaOverview`
- `keywordOverview`

### Suggested core types

- `GraphNode`
- `GraphRelationship`
- `GraphProperty`
- `GraphNodeConnection`
- `GraphRelationshipConnection`
- `GraphSchemaOverview`

### Suggested `GraphNode` fields

- `id`
- `labels`
- `primaryLabel`
- `properties`
- `projectContext`
- `outgoingRelationships(filter, limit)`
- `incomingRelationships(filter, limit)`

### Suggested `GraphRelationship` fields

- `id`
- `type`
- `fromNodeId`
- `toNodeId`
- `properties`

### Suggested filter inputs

- `labels`
- `relationshipTypes`
- `projectContext`
- `propertyEquals`
- `propertyContains`
- `propertyIn`
- `nodeIds`
- `depth`
- `keywordText`
- `keywordCategory`

## Important Modeling Decision

The existing database is heterogeneous.

That means the first schema should not assume every node has the same property set or shape. Prefer:

- a generic node model with:
  - labels
  - property bag
  - stable id
- optional convenience fields for common concepts such as:
  - `projectContext`
  - `name`
  - `type`
  - `filePath`

This is safer than trying to fully map every current Neo4j label into strongly typed GraphQL classes in the first slice.

## Query Construction Decision

The feasible v1 direction is a bounded query builder over the official Neo4j .NET driver, not a LINQ-first design.

Why:

- Hot Chocolate supports custom data integrations through `IExecutable`, so the GraphQL layer can stay transport-focused while Application and Infrastructure own execution.
- The official Neo4j .NET driver exposes query execution and object mapping, but it is query-string based rather than a general LINQ provider.
- The community `Neo4jClient` supports fluent Cypher construction, but it is not the official driver and its published docs are explicitly marked out of date.
- Building v1 on a LINQ-to-Cypher assumption would add risk around dynamic filtering, sorting, and paging behavior without a strong repo-local precedent.

Recommendation:

- use the official Neo4j .NET driver for execution
- add a small internal graph query builder in Infrastructure that composes:
  - allowed labels
  - allowed relationship types
  - parameterized property filters
  - bounded sorting
  - bounded paging
  - bounded traversal clauses
- keep that builder behind `IGraphReadRepository`

Optional future direction:

- if the team later wants a higher-level composition DSL, evaluate whether a narrow fluent Cypher adapter is worth introducing
- do not make community LINQ-to-Cypher support a prerequisite for v1

## Architecture Plan

### 1. Add a generic graph query application seam

Add a new Application service for bounded generic graph reads.

Suggested shape:

- `IGraphQueryService`
- `GraphQueryService`

Responsibilities:

- list labels currently present in the graph
- list relationship types currently present in the graph
- fetch nodes by id or bounded filter
- fetch relationships by bounded filter
- traverse neighbors from a node with depth and limit caps
- expose keyword graph nodes and edges through the same generic DTOs and filters
- return generic DTOs that both GraphQL and MCP can reuse
- enforce read-only query constraints

This keeps GraphQL resolvers thin and keeps MCP from talking directly to Neo4j.

### 2. Add a dedicated generic graph repository seam

The current repo seams are split:

- `ICodeGraphRepository` for code graph
- `IVectorRepository` for knowledge documents

Those are not a full generic graph reader today.

Add a new Core abstraction for generic graph reads:

- `IGraphReadRepository`

Suggested responsibilities:

- list labels
- list relationship types
- read node by id
- read nodes by bounded filter
- read relationships by bounded filter
- traverse neighborhood
- compose parameterized Cypher from bounded filter and sort inputs

Keep raw Cypher in Infrastructure only.

### 3. Implement generic reads in Infrastructure

Add a Neo4j-backed implementation under `src/Infrastructure/Graph/`.

Suggested shape:

- `Neo4jGraphReadRepository`

Important constraints:

- only support read-only queries
- only expose bounded filtering
- only expose bounded sorting over an allowlisted field set
- cap traversal depth
- cap page size
- cap returned property payload size if needed
- centralize query-string assembly so new filters and ordering rules do not require ad hoc Cypher edits everywhere

The implementation can read across existing labels without trying to force all nodes through `CodeNode`.

### 4. Keep existing repositories intact

Do not break or replace:

- `ICodeGraphRepository`
- `IVectorRepository`

The new generic repository should be additive. Existing tools can keep their specialized behavior. The GraphQL layer can reuse specialized repositories later, but the first contract should center on the generic graph seam.

### 5. Add Hot Chocolate only in `src/McpServer`

GraphQL remains a presentation concern.

Implementation direction:

- add Hot Chocolate packages to `src/McpServer`
- register query types in `Program.cs`
- map `/graphql`
- wire resolvers to `IGraphQueryService`
- keep the existing auth middleware in front of the new endpoint

### 6. Add MCP exposure as a thin adapter

After the HTTP GraphQL seam exists, add MCP exposure in one of two ways:

- option A: a generic `query_graphql` MCP tool that accepts:
  - GraphQL document
  - variables JSON
- option B: a smaller MCP tool family over the same Application seam:
  - `list_graph_labels`
  - `query_graph_nodes`
  - `query_graph_relationships`
  - `get_graph_neighbors`

Recommended first MCP direction:

- prefer the smaller fixed MCP tool family first

Reason:

- it is easier to keep deterministic and bounded
- it is easier to test
- it avoids opening an overly broad GraphQL passthrough immediately
- it lets us add a keyword-oriented MCP query surface without exposing raw GraphQL text first

## Safety And Security Constraints

This is the main design risk.

- GraphQL must be read-only in v1.
- No mutation root in v1.
- No raw Cypher passthrough in v1.
- Use the same API key auth as the current API and MCP surfaces.
- Apply strict:
  - query depth limits
  - complexity limits
  - page-size limits
  - traversal limits
- Validate requested labels and relationship types where practical.
- Validate requested sort fields and sort directions against an allowlist.
- Keep cancellation token flow through all read paths.
- Consider limiting property bag size or property count per node if large payloads become a practical issue.

## API Surface Plan

### HTTP

- Add `/graphql` to the current `src/McpServer` host.
- Keep existing `/api/v1/*` endpoints unchanged.
- Do not replace current specialized APIs or MCP tools.

### MCP

- Add MCP exposure only after the GraphQL/Application seam is stable.
- Keep MCP output deterministic and bounded.
- If a generic GraphQL MCP tool is added later, reject mutation operations and unsupported query shapes.

## Schema Strategy

There are two viable schema strategies.

### Strategy A. Strongly typed per-label schema

Examples:

- `CodeNode`
- `KnowledgeDocument`
- `ConfigurationEntry`

Why defer this:

- the database is heterogeneous
- the label/property set can evolve
- the first slice would become schema-modeling work instead of usable graph access

### Strategy B. Generic graph schema

Examples:

- `GraphNode`
- `GraphRelationship`
- `properties: [GraphProperty!]!`

Recommendation:

- choose Strategy B first
- add typed convenience surfaces later for high-value labels if needed

## Test Plan

### Application tests

- [ ] `GraphQueryService` lists existing labels.
- [ ] `GraphQueryService` lists existing relationship types.
- [ ] `GraphQueryService` can fetch a node by id.
- [ ] `GraphQueryService` can filter nodes by label and project context.
- [ ] `GraphQueryService` can filter keyword graph nodes by keyword text or category when those properties exist.
- [ ] `GraphQueryService` can filter relationships by type.
- [ ] `GraphQueryService` can apply bounded sorting only to allowed fields.
- [ ] `GraphQueryService` enforces limit, skip, and depth caps.
- [ ] `GraphQueryService` rejects unsupported or unsafe query shapes.

### Infrastructure tests

- [ ] `Neo4jGraphReadRepository` reads node labels and property bags correctly.
- [ ] `Neo4jGraphReadRepository` reads relationship types and endpoints correctly.
- [ ] `Neo4jGraphReadRepository` can traverse neighbors with bounded depth.
- [ ] the internal query builder parameterizes filters and sorting rather than concatenating unsafe Cypher fragments.
- [ ] keyword graph labels and edges are readable through the generic repository.
- [ ] generic graph reads do not regress current `Neo4jCodeGraphRepository` or `Neo4jVectorRepository` behavior.

### GraphQL endpoint tests

- [ ] authenticated query to `/graphql` can list labels.
- [ ] authenticated query to `/graphql` can fetch nodes and relationships.
- [ ] authenticated query to `/graphql` can fetch keyword graph nodes through the same node query surface.
- [ ] unauthenticated query is rejected.
- [ ] mutation operations are unavailable.
- [ ] depth and complexity limits reject overly broad queries.
- [ ] the generic property-bag result shape remains stable.

### MCP tests

- [ ] each new MCP graph-query tool forwards to the Application seam correctly.
- [ ] keyword-oriented MCP queries forward to the same Application seam correctly.
- [ ] unsupported filters or oversize requests fail cleanly.
- [ ] MCP output stays deterministic and bounded.

### Regression checks

- [ ] existing `/api/v1/knowledge/*`, `/api/v1/status/*`, and `/sse` behavior remains unchanged.
- [ ] existing code-graph and knowledge-document query behavior remains unchanged.

## Documentation Plan

- [ ] Update `docs/how-it-works.md` to show GraphQL as an additional read surface over the Neo4j graph.
- [ ] Update `README.md` with the new GraphQL graph-query capability and auth requirements.
- [ ] Update `docs/features.md` with the new GraphQL and MCP graph-query surfaces.
- [ ] Add one focused usage doc with:
  - example GraphQL queries for labels, nodes, and relationships
  - example GraphQL queries for keyword graph inspection
  - example MCP usage
  - explicit safety limits

## Implementation Phases

### Phase 0. Confirm the contract

- [ ] Decide whether first MCP support is:
  - a generic `query_graphql` tool
  - or a smaller fixed graph-query MCP tool family
- [ ] Decide whether schema introspection remains enabled in production.
- [ ] Decide the initial page-size, depth, and complexity limits.
- [ ] Decide the first allowlisted sort fields and which keyword properties are officially queryable in v1.

### Phase 1. Add generic graph contracts

- [x] Add generic DTOs for graph node, relationship, property, and filter results.
- [x] Add `IGraphReadRepository`.
- [x] Add `IGraphQueryService`.
- [x] Keep the contract generic across existing labels.
- [x] Add bounded sort and filter DTOs instead of transport-specific ad hoc query arguments.

### Phase 2. Add Neo4j generic read implementation

- [x] Implement `Neo4jGraphReadRepository`.
- [x] Implement an internal bounded Cypher query builder used by that repository.
- [x] Add bounded Cypher queries for:
  - labels
  - relationship types
  - nodes
  - relationships
  - neighbors
- [x] ensure keyword graph labels and edges work through the same query path
- [x] keep all GraphQL-specific Cypher in the dedicated `src/Infrastructure/GraphQueries/` surface

### Phase 3. Add Application service

- [x] Implement `GraphQueryService`.
- [x] centralize validation, caps, and allowed query rules there.
- [x] keep transport-specific logic out of Application.

### Phase 4. Add GraphQL presentation layer

- [x] Add Hot Chocolate package references in `src/McpServer`.
- [x] Register GraphQL services in `Program.cs`.
- [x] Add query types and resolvers under a new `src/McpServer/GraphQl/` folder.
- [x] Map `/graphql`.
- [x] Apply auth, depth, complexity, and page-size guards.

### Phase 5. Add MCP adapter

- [ ] Add the chosen MCP graph-query surface in `src/McpServer/Tools/`.
- [ ] delegate directly to `IGraphQueryService`
- [ ] include at least one keyword-oriented MCP query path or prove the generic node filters cover keyword needs cleanly
- [ ] add response-shape and bounds tests

### Phase 6. Docs and rollout

- [ ] Add examples that show:
  - list labels
  - fetch nodes by label
  - fetch keyword nodes by property filter
  - fetch relationships by type
  - traverse neighbors
- [ ] explain the read-only and bounded design clearly

## Open Questions

- Should MCP v1 expose generic GraphQL text, or only a fixed family of graph query tools?
- Should the GraphQL node property bag be returned as:
  - a list of key/value entries
  - or a JSON scalar
- Should the schema expose all labels equally, or should some infrastructure-only labels be hidden in v1?
- Should schema introspection remain enabled outside local development?

## Recommended First Implementation Slice

Keep the first delivery smaller than the full vision:

- [x] Add `IGraphReadRepository`
- [x] Add `IGraphQueryService`
- [x] Add bounded generic DTOs for nodes, relationships, and properties
- [x] Add bounded generic DTOs for filters and sorting
- [x] Add read-only `/graphql`
- [x] expose:
  - `labels`
  - `relationshipTypes`
  - `node(id)`
  - `nodes(filter, limit, skip)`
  - `relationships(filter, limit, skip)`
  - `neighbors(nodeId, relationshipTypes, direction, limit, depth)`
- [x] make sure the generic `nodes` query can cover keyword graph use cases from day one
- [x] defer generic GraphQL passthrough in MCP until the fixed graph-query tool family is proven

Reason:

- it exposes real current graph value quickly
- it avoids over-committing to a too-broad schema or passthrough surface
- it keeps the safety model simpler

## Success Criteria

- [x] An authenticated client can query existing Neo4j labels, nodes, properties, and relationships through `/graphql`.
- [x] The same generic query surface can return keyword graph data without a separate keyword-only transport stack.
- [x] The first GraphQL schema is read-only and bounded.
- [x] The schema can return generic node and relationship data for current graph content, not only documents.
- [ ] MCP can reuse the same underlying query seam.
- [x] Existing API and MCP behavior does not regress.
- [x] Tests cover Application behavior, Infrastructure reads, GraphQL transport, auth, and MCP forwarding if MCP lands in the same slice.

## Definition Of Done

- [x] Hot Chocolate is registered in `src/McpServer` and `/graphql` is live.
- [x] The GraphQL schema can query the existing Neo4j graph generically across labels, properties, and relationships.
- [x] Keyword graph nodes and edges are queryable through the same generic graph surface.
- [x] The schema is read-only, authenticated, and bounded by depth, complexity, and page-size rules.
- [x] At least one MCP graph-query surface reuses the same Application seam, or the plan explicitly lands the HTTP GraphQL foundation first with MCP as the next slice.
- [ ] Docs explain how to query the existing graph and what safety limits apply.
- [x] Regression tests prove the new capability without breaking current knowledge-ingest, status API, or MCP flows.
