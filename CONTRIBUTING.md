# Contributing to CodeMeridian

Thank you for contributing to CodeMeridian.

CodeMeridian helps AI coding agents understand a repository before editing it by indexing code, documentation, diagnostics, and relationships into a queryable graph.

This guide describes how to keep contributions focused, testable, and useful.

## Before You Start

Before making a non-trivial change:

1. Read `README.md`.
2. Read `AGENTS.md`.
3. Check the existing feature documentation.
4. Inspect the current code before proposing edits.
5. Use CodeMeridian itself when possible to understand impacted files, symbols, tests, and docs.

Small typo fixes, documentation cleanup, and obvious maintenance changes do not need a full analysis step.

## Contribution Principles

Good contributions should improve one or more of these areas:

* indexing accuracy
* graph relationship quality
* MCP tool usefulness
* CLI reliability
* configuration clarity
* diagnostics and troubleshooting
* documentation quality
* test coverage
* performance on real repositories

Keep changes focused. Avoid broad rewrites unless there is a clear issue or accepted design reason.

## Architecture Guidelines

Keep responsibilities separated.

* Indexers collect source facts.
* Application services coordinate use cases.
* MCP tools expose focused queries.
* Persistence code owns Neo4j-specific access.
* CLI and host code should stay thin.
* Tests should verify behavior, not implementation noise.

Avoid leaking persistence details into higher-level workflows unless the existing architecture already allows it.

## AI-Assisted Development

AI-assisted contributions are welcome, but agents should not guess when the repository can be inspected.

For non-trivial changes, agents should:

* inspect relevant files before editing
* identify likely impacted callers, tests, and docs
* explain why each touched file is in scope
* make the smallest safe change
* update tests or docs when behavior changes
* avoid unsupported claims about correctness
* run relevant checks when possible

Useful agent prompt:

```text
Use the repository context before editing.

Find the files, symbols, callers, tests, and docs related to this change.
Propose the smallest safe edit.
Explain why each touched file is in scope.
After the change, list the checks that should be run.
```

## Coding Guidelines

Use modern C# and .NET practices:

* prefer clear names over clever abstractions
* keep methods small and focused
* use async APIs for I/O
* pass `CancellationToken` through I/O paths
* avoid `.Result` and `.Wait()`
* handle nullable reference types intentionally
* prefer constructor injection over hidden service creation
* keep logging structured
* avoid logging secrets, tokens, credentials, file contents, or sensitive user data

## Logging

Use structured logging.

Good:

```csharp
_logger.LogInformation(
    "Indexed project {ProjectName} with {SymbolCount} symbols",
    projectName,
    symbolCount);
```

Avoid interpolated logging:

```csharp
_logger.LogInformation($"Indexed project {projectName}");
```

Inside loops, prefer `Trace` and log one summary outside the loop.

## Testing

Add or update tests when changing behavior.

Important areas to test:

* symbol extraction
* relationship creation
* graph queries
* MCP tool responses
* configuration loading
* CLI behavior
* Neo4j persistence behavior
* edge cases around missing files, partial projects, and stale indexes

Prefer deterministic tests over broad snapshots when possible.

## Documentation

Update documentation when changing:

* CLI commands
* MCP tools
* configuration
* install flow
* graph schema
* indexing behavior
* agent workflow guidance

Documentation is part of the product because CodeMeridian exists to reduce stale project context.

## Build and Validation

Before submitting a change, run the relevant checks for the area you changed.

At minimum, use the existing solution/test commands documented in the repository.

For code changes, prefer:

```bash
dotnet build
dotnet test
```

If the change affects package output, CLI behavior, Docker, or MCP server behavior, run the matching local validation flow as well.

## Language Around the Project

Prefer accurate wording:

* local graph memory layer
* project map for AI coding agents
* repository knowledge graph
* deterministic context layer
* graph-assisted development workflow

Avoid overclaiming:

* autonomous development
* replaces tests
* replaces code review
* fully understands the codebase

The goal is simple:

CodeMeridian helps agents ask better questions before they edit.
