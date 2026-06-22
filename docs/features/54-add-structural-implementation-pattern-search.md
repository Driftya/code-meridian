# Add Structural Implementation Pattern Search

- Status: done
- Priority: P2
- Note: Find existing implementation shapes that match a requested feature without relying only on text similarity.

**Feature:** `find_implementation_patterns`

**Why Neo4j helps:** Structural similarity is more useful than lexical similarity when an agent needs a proven example to copy from. Neo4j can compare graph neighborhoods such as endpoint -> service -> repository -> tests and explain why a pattern match is relevant.

## Goal

Help agents answer questions like "what existing feature is implemented like this new one?" by returning structurally similar slices, not just semantically similar files.

## Language-Neutral Requirements

- Work over graph neighborhoods produced by both Roslyn and TsIndexer.
- Use shared graph concepts such as endpoints, services, repositories, tests, commands, docs, external concepts, and database operations.
- Avoid parser-specific heuristics that only work for one language.

## Expected Output

- Ranked implementation patterns with a similarity score
- The matched shape, such as endpoint/service/repository/test or command/handler/store/test
- A short evidence summary describing the shared structure
- Confidence labels that separate exact structural matches from weaker related patterns

## Example

```text
Query: add a new invite acceptance flow

Closest implementation patterns:
1. Between Us feature
2. Invite feature
3. Reports feature
```

Each result should explain the neighborhood it matched, for example:

- route or entry point
- application or domain service
- repository or store boundary
- persistence or external system edge
- direct tests or test shield

## Suggested Scope

- Start with structural pattern matching around known entry points and service seams.
- Reuse existing graph signals from `find_implementation_surface`, `find_duplicate_candidates`, and `trace_endpoint` where possible.
- Prefer deterministic scoring over opaque embeddings-only ranking.
- Allow mixed-language matches when the graph shape is otherwise similar.

## Delivered

- Added the `find_implementation_patterns` tool and application service flow.
- The result combines lexical seeds with optional embedding seeds, then reranks by graph structure instead of relying on similarity alone.
- Structural evidence now highlights entry points, application or domain behavior, contracts, repositories or stores, external boundaries, and related tests.
- Added unit coverage for ranking and fallback behavior, MCP forwarding coverage, workflow-planning coverage, and Neo4j integration coverage for the repository query.
