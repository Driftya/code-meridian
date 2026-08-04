# Coding And Testing Conventions

## Design Expectations

- Prefer simple, direct solutions.
- Keep changes surgical and traceable to the request.
- Use constructor injection rather than `new ConcreteType()` in application code.
- Keep MCP tools factual. They return data, not model reasoning.

## MCP Tool Conventions

- Tool names use snake case such as `find_impact`.
- Parameter names use `camelCase`.
- Return `Task<string>` with markdown output by default.
- When a tool exposes structured content, preserve a useful text response for clients that do not consume `structuredContent`, advertise an `outputSchema`, and keep MCP SDK types in `McpServer`.
- Build structured and Markdown responses from the same protocol-neutral result DTO. Do not parse Markdown to recover structured facts.
- Give independently evolving structured contracts an explicit string `ContractVersion`. Keep collection properties non-null, serialize declared nullable properties consistently with the advertised schema, and treat removal or incompatible type changes as breaking within that version.
- Empty results should return guidance, not empty strings or exceptions.
- Add `[Description]` to each tool and parameter.
- Every `[McpServerTool]` must set a human-readable `Title` and explicitly classify `ReadOnly`, `Destructive`, `Idempotent`, and `OpenWorld` behavior.
- Classify the behavior the implementation can actually perform. Conditional mutation makes a tool mutating unless the implementation always refuses that path.
- Treat annotations as client hints, not authorization. Destructive tools still require server-side authentication, validation, and confirmation where appropriate.
- Use `OpenWorld = false` for operations restricted to CodeMeridian-controlled graph, document, configuration, or checked-in data.
- Long-running tool implementations must accept and propagate `CancellationToken`. MCP Tasks remain an adapter concern in `McpServer`; Application services must not depend on the MCP task store.
- Apps are optional presentation adapters. Keep the underlying tool useful as text/structured data, keep App registration feature-flagged while experimental, and never embed API keys in UI resources.
- Request filters and telemetry may record bounded tool identity, category, duration, and outcome. Do not log full arguments, source snippets, document bodies, project names as metric labels, or credentials.

Representative annotations:

```csharp
[McpServerTool(Name = "find_impact", Title = "Find Impact",
    ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]

[McpServerTool(Name = "clear_code_graph", Title = "Clear Code Graph",
    ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
```

Representative structured-result layout:

```text
Application/Services/ConnectionAnalysisResult.cs     public aggregate result and Markdown formatter
Application/Services/ConnectionNodeResult.cs         public node item
Application/Services/ConnectionEdgeResult.cs         public edge item
McpServer/Tools/StructuredToolResult.cs               protocol serialization adapter
```

Keep one public result or item type per file. The Application records contain facts and deterministic formatting only; `CallToolResult`, `structuredContent`, SDK attributes, and JSON Schema advertisement remain in `McpServer`.

## File Size Guidelines

Keep files small and context-friendly.

- One public type per file.
- Prefer files under 300 lines.
- Review files over 500 lines for splitting.
- Files over 800 lines require a reason.
- Generated files, migrations, snapshots, and lock files are exempt.
- Large documentation must use headings so it can be chunked by section.

## Testing Conventions

- Framework: xUnit + NSubstitute + FluentAssertions
- Test classes should be `sealed`
- Keep one behavior per test
- Use `[Theory]` and `[InlineData]` for boundary cases
- Unit tests in this repo should stay isolated from live dependencies unless the project already defines integration coverage

## What To Test

| Concern | Location |
|---|---|
| service formatting and orchestration | `tests/CodeMeridian.Application.Tests/Services/` |
| domain model invariants | `tests/CodeMeridian.Core.Tests/CodeGraph/` |
| registry behavior | `tests/CodeMeridian.Application.Tests/Extensions/` |
| Neo4j behavior | integration tests against real Neo4j |

## Adding A New Graph Tool

1. Add the repository contract.
2. Implement the Cypher in infrastructure.
3. Add the application service method and markdown formatting.
4. Expose it from the MCP tool class with a stable name, human-readable title, description, and reviewed behavior annotations.
5. Add tests for empty and happy paths.
6. Add or update the MCP discovery contract test so the registered tool inventory and annotations remain intentional.
7. If the tool returns structured content, test both its text fallback and schema-valid structured result.
8. Update user-facing documentation.
