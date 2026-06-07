# Cross-Language Route Matching Plan

Date: 2026-06-05

## Goal

Improve CodeMeridian's ability to connect frontend code to backend C# endpoints without pretending route matching is always exact.

The useful outcome is:

```text
Frontend component -> HTTP client call -> API route concept -> ASP.NET endpoint handler
```

This should help `find_connection`, `find_implementation_surface`, and `build_minimal_context` answer questions like:

```text
Which backend endpoint does this React component call?
What frontend files depend on this C# endpoint?
What is the full-stack impact of changing this route?
```

## Why This Is Hard

Route matching is partly static and partly runtime behavior.

Reliable static cases:

- Literal frontend URLs such as `fetch("/api/orders")`.
- Axios calls with literal paths such as `axios.post("/api/orders", body)`.
- ASP.NET minimal API literals such as `app.MapPost("/api/orders", HandleOrder)`.
- Controller attributes with literal templates such as `[Route("api/orders")]`.

Hard cases:

- Base URLs from environment variables.
- Client wrappers such as `apiClient.post(OrderRoutes.create(), body)`.
- Template strings with arbitrary expressions.
- Route groups and conventions.
- Controller routes built from constants.
- Reverse proxies, API gateways, and BFF layers.
- Generated clients from OpenAPI or NSwag.

The first implementation should treat exact static matches as graph edges and label everything else with confidence.

## Graph Model

Add or reuse route-oriented nodes:

- `ApiEndpoint`: canonical route node, for example `api:POST /api/orders/{id}`.
- `Method`: backend handler or controller action.
- `Method` / `Class` / `File`: frontend caller node.

Suggested edges:

- Backend handler `HandlesRoute` -> `ApiEndpoint`
- Frontend call site `CallsRoute` -> `ApiEndpoint`
- Generated client method `WrapsRoute` -> `ApiEndpoint`
- Route constant or helper `DefinesRoute` -> `ApiEndpoint`

Every route edge should carry:

- `method`: `GET`, `POST`, `PUT`, `PATCH`, `DELETE`, or `ANY`
- `routeTemplate`: normalized route template
- `confidence`: `exact`, `normalized`, `constant`, `wrapper`, or `heuristic`
- `source`: `aspnet-minimal-api`, `aspnet-controller`, `fetch`, `axios`, `http-client-wrapper`, `openapi`
- `lineNumber`

## Route Normalization

Normalize both frontend and backend routes before matching:

- Lowercase route literals for matching, but preserve original display text.
- Remove scheme, host, and query string.
- Remove duplicate slashes.
- Normalize trailing slash away except for `/`.
- Convert `:id`, `${id}`, `{id:int}`, and `{id}` into `{param}`.
- Keep HTTP method as part of the key.

Examples:

```text
POST https://api.example.com/api/orders/42?expand=true -> POST /api/orders/{param}
GET /api/orders/{id:int} -> GET /api/orders/{param}
GET `/api/orders/${orderId}` -> GET /api/orders/{param}
```

Do not match different HTTP methods unless one side is `ANY`.

## C# Backend Extraction

Use Roslyn, not regex, for the C# side.

Minimal APIs:

- Detect calls named `MapGet`, `MapPost`, `MapPut`, `MapPatch`, `MapDelete`, `MapMethods`, `MapGroup`.
- Extract literal route templates from the first argument.
- Compose `MapGroup("/api").MapGet("/orders", ...)` into `/api/orders`.
- Resolve simple `const string` route values when local and static.
- Link the endpoint to the delegate method, lambda location, or handler method when available.

Controllers:

- Detect `[Route]`, `[HttpGet]`, `[HttpPost]`, `[HttpPut]`, `[HttpPatch]`, `[HttpDelete]`.
- Compose controller-level and action-level route templates.
- Handle `[controller]` and `[action]` tokens for common cases.
- Link to the action method.

Initial exclusions:

- Custom route conventions.
- Runtime-computed route attributes.
- Deep constant propagation across projects.
- Middleware routing behavior that cannot be statically proven.

## TypeScript Frontend Extraction

Use the TypeScript AST through `ts-morph`, not regex.

Direct calls:

- `fetch("/api/orders")`
- `fetch(url, { method: "POST" })`
- `axios.get("/api/orders")`
- `axios.post("/api/orders", body)`
- `client.get("/api/orders")` when the method name is HTTP-like.

Import-aware wrappers:

- Identify functions that wrap `fetch`, `axios`, or HTTP-like client methods.
- If a wrapper has a literal route argument or returns a route literal, index it as a route caller.
- If a wrapper accepts a path parameter, index the wrapper with lower confidence.

Constants and helpers:

- Resolve local `const route = "/api/orders"` when used in a call.
- Resolve simple template strings with identifiers as route params, for example `` `/api/orders/${id}` ``.
- Recognize route maps such as `const routes = { orders: "/api/orders" }` for local same-file usage first.

Initial exclusions:

- Arbitrary function execution.
- Runtime env construction beyond stripping known base URLs.
- Deep inter-file dataflow.
- Full framework-specific router inference.

## Library-Agnostic Strategy

Do not hardcode only `fetch` and `axios`.

Use layered detection:

1. Known APIs: `fetch`, `axios.*`.
2. HTTP method names: `.get`, `.post`, `.put`, `.patch`, `.delete`, `.request`.
3. Config object method fields: `{ method: "POST" }`.
4. URL-like string argument: starts with `/`, `http://`, `https://`, or known API prefix.
5. Imported wrapper names from config later, for example `httpClient`, `api`, `request`.

This makes the first version work with many libraries while still labeling confidence accurately.

## Matching Rules

Confidence levels:

- `exact`: same HTTP method and normalized route template from literals.
- `normalized`: same method and route shape after parameter normalization.
- `constant`: one or both sides came from locally resolved constants.
- `wrapper`: frontend route came through a detected HTTP wrapper.
- `heuristic`: route shape is plausible but not statically proven.

Only `exact`, `normalized`, and `constant` should affect high-confidence `find_connection` paths by default.

`wrapper` and `heuristic` should be shown as hints in context packs, not hard dependency facts, unless the user asks for full detail.

## Implementation Phases

Phase 1: backend route index

- Add `ApiEndpoint` nodes from ASP.NET minimal APIs and controllers.
- Add `HandlesRoute` edges from handler methods/actions to endpoint nodes.
- Add integration tests using this repository's MCP API routes.

Phase 2: frontend route calls

- Add TypeScript extraction for direct `fetch`, `axios`, and HTTP-like method calls.
- Add `CallsRoute` edges to endpoint nodes when a backend match exists.
- Add unresolved `ApiEndpoint` nodes for calls without a backend match.

Phase 3: constants and wrappers

- Resolve local constants and simple route maps.
- Detect simple HTTP wrapper functions.
- Add confidence labels and display them in `find_connection` and `build_minimal_context`.

Phase 4: optional OpenAPI support

- Import OpenAPI specs as route truth when present.
- Use generated client names to link frontend methods to API endpoints.
- This may be more reliable than heuristic scanning for many full-stack projects.

## When To Defer

Defer broad implementation if the first version cannot keep false positives low.

Reasons to wait:

- The repo mostly uses generated API clients where OpenAPI import would be better.
- Route construction depends heavily on runtime config.
- Most frontend calls go through opaque wrappers with dynamic paths.
- The C# backend uses custom routing conventions that Roslyn cannot infer cleanly.

In those cases, implement OpenAPI import or explicit route-link configuration before heuristic matching.

## Recommended First Slice

Start with Phase 1 only:

- ASP.NET minimal API route extraction.
- Controller attribute route extraction.
- `ApiEndpoint` nodes and `HandlesRoute` edges.
- Query output in `find_connection`.

This gives useful backend route visibility without risking noisy frontend matches. Then add frontend direct literal matching as the second slice.
