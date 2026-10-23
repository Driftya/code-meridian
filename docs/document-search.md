# Scoped Document Search

`search_documentation` searches indexed text without embeddings. It accepts `query`, optional `projectContext`, and optional `rootPath` and `targetPath`. Supplying either path requires an explicit project. It reads uploaded documents and inventory; it does not read local files, fetch URLs, index content, or validate Markdown references.

## Scope

| Input | Search boundary |
|---|---|
| No path filters | Existing project-wide search; complete project document evidence is required |
| `rootPath: "docs/design"` | Files beneath that directory, including nested directories |
| `targetPath: "docs/design/A.md"` | Chunks belonging to that exact file |
| Both paths | Exact target, which must be beneath the root |
| `rootPath: "."` | Entire admitted project document scope |

Paths are repository-relative inventory identities with ordinal case. Separators normalize to `/`; spaces and case are preserved. Absolute paths, drive/URI paths, empty paths, repeated/trailing separators, control characters, and `.`/`..` segments are rejected (except the whole root `.`). A root uses directory-segment boundaries: `docs` does not include `docs-old`. Targets must be observed Markdown/text files. Unknown, missing, or excluded targets cannot establish ready coverage.

The Neo4j full-text query applies project and source-path predicates before its result limit, so stronger matches in unrelated roots do not displace scoped results. Application checks reject out-of-scope, cross-project, malformed, removed, and non-document sources before rendering. Search returns at most eight chunks with content previews of at most 600 characters; several chunks can share a file identity. Results are bounded search matches, not an exhaustive listing or a validation of all document content.

## Coverage And Revalidation

A scoped query can use a current successful full `documents` outcome for the same scope or a covering parent. Its analyzer/parser fingerprint and source binding must match the published profile. Every overlapping outcome must also be current and complete. Failed, partial, running, delta, unaudited, or stale overlapping evidence blocks the search; unrelated roots do not contribute readiness or block a confined query. A set of child outcomes cannot authorize a broader root or project query.

Inventory generation, exact target, and the same scope requirements are checked again after the query, including empty results. Changed evidence discards results with a `stale` status. Query failure returns `failed`; cancellation propagates. A legitimate empty result says `ok` and identifies the evaluated scope. No flags authorize automatic refresh or indexing. The existing CLI can still publish project-wide document outcomes; this query support does not implement new per-root producer aggregation.

Document-review workflow plans pass the observed exact file into both coverage evaluation and storage. Searches used for broader code workflows and composite reports retain their existing project boundary unless explicitly narrowed by their caller.

## Interfaces

MCP example:

```json
{
  "query": "authentication decision",
  "projectContext": "MyProject",
  "rootPath": "docs/design"
}
```

Authenticated REST: `GET /api/v1/knowledge/documents/search`, with URL-encoded `query`, `projectContext`, `rootPath`, and `targetPath` query parameters. The response is Markdown. Invalid scope returns HTTP 400; unavailable analysis returns its factual status text instead of clean findings.

SDK example:

```csharp
var report = await client.SearchDocumentationAsync(
    "authentication decision", "MyProject",
    targetPath: "docs/design/A.md", cancellationToken: cancellationToken);
```

Existing unfiltered service/MCP calls retain their defaults. Application callers can supply the new paths by name after the existing cancellation argument. Explicit legacy construction without profiles retains manual/advisory access; production dependency injection supplies the profile service. Live Neo4j release acceptance and root-scoped graph traversal remain separate checklist gates.
