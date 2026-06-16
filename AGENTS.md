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

### Trigger rules

| Situation | Tool to call |
|-----------|--------------|
| Before any non-trivial edit | `build_minimal_context` |
| Before editing a specific method/class | `resolve_exact_symbol`, then `get_context_for_editing` |
| Before a refactor | `find_impact` and `find_test_shield` |
| Before deleting code | `find_unreferenced` |
| Before starting a feature | `find_implementation_surface` |
| Before trusting exact file targets | `check_graph_freshness` or `find_graph_drift` |
| "How do X and Y relate?" | `find_connection` |
| Looking for duplicate/refactor risk | `find_duplicate_candidates` or `find_similar_nodes` |
| Looking for missing tests | `find_coverage_gaps` |
| Searching docs/decisions | `search_documentation` |
| Working with config | `find_config_definitions` and `find_config_usage` |
| Looking for keyword-related context | `find_related_knowledge` |

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
