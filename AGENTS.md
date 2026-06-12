# AGENTS.md

Short always-on guidance for automated agents working in this repository.

Read this first. Use the detailed references under `docs/agent/` when the task needs more depth.

## Behavior Expectations

- Do not assume silently. State important assumptions.
- If something is unclear, ask instead of guessing.
- Prefer the simplest solution that solves the request.
- Do not add speculative abstractions, flexibility, or features.
- Make surgical changes only. Avoid unrelated refactors.
- Match the existing style unless the task requires otherwise.
- Remove only unused code introduced by your own change unless asked.
- Define success criteria and verify them.
- For bugs, prefer reproduce -> fix -> verify.
- Every changed line should trace back to the request.

## Required CodeMeridian Usage

Use CodeMeridian proactively. Prefer graph tools over terminal scans when the graph can answer the question.

| Situation | Tool |
|---|---|
| Unfamiliar area at session start | `get_architectural_overview` |
| Need a node ID | `query_codebase` or `resolve_exact_symbol` |
| Before editing a method or class | `get_context_for_editing` |
| Before a refactor or risky edit | `find_impact` |
| Before suggesting a deletion | `find_unreferenced` |
| Writing tests | `find_coverage_gaps` |
| Comparing structure or relationships | `find_connection` |
| Measuring size or refactor risk | `find_large_nodes`, `find_god_classes` |
| Integrating with an external DB, API, or topic | `link_external_concept` |

Store durable findings with `ingest_document` when they may help future sessions.

## Repository Rules

- Respect Clean Architecture boundaries.
- `Core` must not depend on infrastructure or framework packages.
- Keep Cypher in `src/Infrastructure/Graph/`.
- MCP tools return facts and markdown, not LLM-generated reasoning.
- Follow existing style. Make surgical changes.

## File Size Guidelines

Keep files small and context-friendly.

- One public type per file.
- Prefer files under 300 lines.
- Review files over 500 lines for splitting.
- Files over 800 lines require a reason.
- Generated files, migrations, snapshots, and lock files are exempt.
- Large documentation must use headings so it can be chunked by section.

## How To Use CodeMeridian Here

Typical flow:

1. Resolve the target with `query_codebase` or `resolve_exact_symbol`.
2. Inspect local context with `get_context_for_editing`.
3. Check blast radius with `find_impact`.
4. Use file reads only after the graph has narrowed the surface.

## References

- [Agent Docs Index](docs/agent/README.md)
- [CodeMeridian Usage](docs/agent/codemeridian-usage.md)
- [Behavior Expectations](docs/agent/behavior.md)
- [Architecture Rules](docs/agent/architecture.md)
- [Coding And Testing Conventions](docs/agent/conventions.md)
- [Local Dev And Operations](docs/agent/local-dev.md)
