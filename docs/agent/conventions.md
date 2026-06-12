# Coding And Testing Conventions

## Design Expectations

- Prefer simple, direct solutions.
- Keep changes surgical and traceable to the request.
- Use constructor injection rather than `new ConcreteType()` in application code.
- Keep MCP tools factual. They return data, not model reasoning.

## MCP Tool Conventions

- Tool names use snake case such as `find_impact`.
- Parameter names use `camelCase`.
- Return `Task<string>` with markdown output.
- Empty results should return guidance, not empty strings or exceptions.
- Add `[Description]` to each tool and parameter.

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
4. Expose it from the MCP tool class.
5. Add tests for empty and happy paths.
6. Update user-facing documentation.
