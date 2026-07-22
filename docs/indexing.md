# Indexing Projects

The unified indexer CLI scans a target directory and runs the available language indexers. It currently supports C#, TypeScript/TSX, and documentation ingestion.

## Start the Server

Run `codemeridian serve` in a dedicated runtime folder for the shared backend, not inside every indexed repository.

```powershell
codemeridian serve
```

This starts:

- Neo4j browser: `http://localhost:47474`
- Neo4j bolt: `bolt://localhost:47687`
- MCP server: `http://localhost:5100/sse`
- Swagger UI: `http://localhost:5100/swagger`
- OpenAPI document: `http://localhost:5100/openapi/v1.json`
- GraphQL endpoint: `http://localhost:5100/graphql`

## Install the Indexer Tool

For complete installation options, see [Installation](installation.md).

After `CodeMeridian.Indexer` is published to NuGet.org or another configured NuGet feed, install it as a .NET global tool:

```powershell
dotnet tool install --global CodeMeridian.Indexer
```

Until the package is published, build and install it from this checkout:

```powershell
dotnet pack tools/Indexer -o artifacts/packages
dotnet tool install CodeMeridian.Indexer --global --add-source artifacts/packages
```

See [Publishing the Indexer Tool](publishing.md) for package publishing steps.

After install:

```powershell
codemeridian --list-capabilities
codemeridian serve
codemeridian index . --dry-run
codemeridian index . --clear
codemeridian index . --skip-keywords
codemeridian doctor --project CodeMeridian
codemeridian report --project CodeMeridian
codemeridian check-drift --project CodeMeridian --fail-on high
codemeridian clear --project CodeMeridian
```

## Source Checkout Usage

You can also run the unified indexer directly from this repository:

```powershell
dotnet run --project tools/Indexer -- . --clear
```

## Common Commands

Create local runtime files and start the local backend stack:

```powershell
codemeridian serve
```

Generate runtime files without starting Docker:

```powershell
codemeridian serve --no-start
```

Create project-local indexer and MCP client config in the repository you actually want to index:

```powershell
codemeridian init .
```

Create user-wide fallback config once for many repositories:

```powershell
codemeridian init --global --url http://localhost:5100
```

Index the current directory:

```powershell
codemeridian index . --clear
```

Index another project:

```powershell
codemeridian index C:\Projects\MyApi --clear
```

Set an explicit project context:

```powershell
codemeridian index C:\Projects\MyApi --project MyApi --clear
```

Clear existing graph data before indexing:

```powershell
codemeridian index . --clear
```

Clear without indexing:

```powershell
# Remove one project's code graph and documentation.
codemeridian clear --project MyApi

# Remove all indexed CodeNode graph data across every project.
# Documentation KnowledgeDocument nodes are preserved.
codemeridian clear --all-code-graph
```

Keep indexing while you work:

```powershell
codemeridian index . --watch
```

Show what would be indexed:

```powershell
codemeridian index . --dry-run
```

Check backend health and graph counts:

```powershell
codemeridian doctor --project CodeMeridian
```

Generate an architecture weather report:

```powershell
codemeridian report --project CodeMeridian
```

Verify graph drift before implementation or in CI:

```powershell
codemeridian check-drift --project CodeMeridian --fail-on high
codemeridian index . --verify --project CodeMeridian --fail-on moderate
```

## Options

| Flag | Description |
|------|-------------|
| `--project <name>` | Project context name. Auto-detected from `package.json`, `.sln`, `.slnx`, `.code-workspace`, or folder name |
| `--url <url>` / `--CodeMeridian <url>` | CodeMeridian server URL. Default: `CodeMeridian_Url` from `.env`, or `http://localhost:5100` |
| `--clear` | Wipe this project's existing graph data before indexing |
| `--skip-csharp` | Skip C# indexing |
| `--skip-typescript` | Skip TypeScript / TSX indexing |
| `--no-docs` / `--skip-docs` | Skip documentation ingestion |
| `--dry-run` | Show what would be indexed without ingesting anything |
| `--list-capabilities` | Show available indexers on the current machine |
| `--include-diagnostics` | Run diagnostics indexing. This is the default; kept for compatibility |
| `--skip-diagnostics` | Skip project-native compiler, TypeScript, and lint diagnostics indexing |
| `--allow-repo-scripts` | Allow repo-controlled `dotnet build` and lint commands during diagnostics |
| `--verify` | Skip indexing and only verify graph drift/freshness |
| `--fail-on <severity>` | Verification drift threshold: `low`, `moderate`, or `high` |
| `--no-incremental` / `--force-full` | Ignore the local file cache and scan all enabled files |
| `--watch` | Stay running and re-index when files change |

## Clear Commands

Use `codemeridian index --clear` for normal rebuilds. It clears the selected project once before indexing, which avoids stale nodes after file moves, renames, or indexer ID changes.

Use `codemeridian clear --project <name>` when you want to remove one project's indexed code graph and documentation without immediately re-indexing.

Use `codemeridian clear --all-code-graph` only for a hard reset of indexed code nodes across every project. It removes `CodeNode` nodes and relationships but preserves documentation nodes.

## Incremental Indexing

By default, the unified indexer stores a language-neutral file snapshot in `.meridian/cache`. Later runs compare file path, last-write time, and length so unchanged files can be skipped before invoking the C# or TypeScript indexer.

Changed and deleted files are removed from the project graph before re-indexing, which prevents old symbols from lingering when a file is edited or removed. Use `--clear` after major renames, project moves, or indexer ID changes. Use `--no-incremental` or `--force-full` when you want a full scan without clearing existing knowledge.

For C#, node ingestion and relationship resolution have separate scopes. Only changed files own and upsert their file-backed nodes, but every current C# file participates in the resolution catalog. `Contains` and declaration edges are owned by their source file. Cross-file `Calls`, `Uses`, `Implements`, and inheritance edges are reconstructed from the surviving source declarations, so deleting or changing a target cannot permanently erase an incoming edge from an unchanged source. Synthetic targets without a file are ingested only when a changed source reaches them. Each full or incremental pass records scanned/ingested counts and resolved/unresolved relationship diagnostics in project-scoped index-run metadata.

## C# Indexing

The C# indexer uses Roslyn syntax trees. It does not require a successful project build.

It extracts:

- Files
- Namespaces
- Classes
- Interfaces
- Enums
- Methods
- Properties
- Containment relationships
- Inheritance and implementation relationships
- Best-effort call relationships

## TypeScript / TSX Indexing

The TypeScript indexer uses ts-morph. The packaged .NET tool includes the TypeScript indexer source and restores its npm dependencies on the first TypeScript indexing run if needed.

It extracts:

- Files
- Classes
- Interfaces
- Methods/functions
- Properties
- Enums
- Import dependencies
- Inheritance and implementation relationships
- File-to-class/function containment

Node.js 18+ is required for TypeScript / TSX indexing.

## Documentation Ingestion

The indexer ingests README and documentation files unless disabled with `--no-docs` or `--skip-docs`.

Documentation becomes searchable through `search_documentation`.

The REST ingestion API used by the packaged indexers supports both single-item and bulk POSTs. Current bulk endpoints are:

- `POST /api/v1/knowledge/nodes/bulk`
- `POST /api/v1/knowledge/nodes/edges/bulk`
- `POST /api/v1/knowledge/documents/bulk`

## Keyword Refresh

When keyword enrichment is enabled, the MCP server queues an incremental keyword refresh after code nodes and documents are ingested. The queue batches and debounces changed source nodes, refreshes derived keyword relationships for those nodes, recalculates project keyword statistics, and then runs classification for the affected project.

The index command also submits a keyword rebuild/classification job after a successful run by default. Use `--skip-keywords` when you want only structural indexing.

Manual keyword commands remain useful for repair, rule changes, and full refreshes:

```powershell
codemeridian keywords rebuild --project CodeMeridian
codemeridian keywords classify --project CodeMeridian
```

## Diagnostics Indexing

Diagnostics indexing runs by default so compiler, analyzer, TypeScript, and lint findings stay attached to the graph.
Repo-controlled build and lint commands are only executed when `--allow-repo-scripts` is set.

Run normal indexing:

```powershell
codemeridian index .
```

Skip diagnostics when you need a faster structural-only index:

```powershell
codemeridian index . --skip-diagnostics
```

The unified indexer refreshes diagnostic nodes for the project, then runs available project-native checks:

- `dotnet build --no-restore --nologo` for C# compiler and analyzer diagnostics
- local `tsc --noEmit --pretty false` when `tsconfig.json` and `node_modules/.bin/tsc` are present
- `npm run lint` when `package.json` defines a lint script, otherwise local `eslint .` when available

Without `--allow-repo-scripts`, the indexer skips `dotnet build` and repo lint commands but still uses the local `tsc` check when available.

Diagnostics are stored as `Diagnostic` code nodes with severity/code, source tool, message, file, and line. When backend embeddings are enabled, diagnostic messages are embedded during ingestion for semantic discovery. Diagnostics can be queried with `find_diagnostics` and `find_diagnostics_for_node`.

## `codemeridian doctor`

`codemeridian doctor` asks the backend to inspect the current project graph and report:

- whether the server and backend endpoint are reachable
- whether Neo4j is reachable from the backend
- how many code nodes, call edges, docs, and diagnostics are indexed
- whether graph drift looks low, moderate, or high
- whether embeddings are enabled and which provider is active

It does not talk to Neo4j directly from the client. The backend does the check so the result reflects the actual running MCP service.

## `codemeridian report`

`codemeridian report` asks the backend for a compact architecture weather report. It is distinct from `doctor`: `doctor` checks operational health, while `report` summarizes architecture signals from the graph.

The report includes:

- code-node and call-relationship counts
- namespace cycles
- architecture violations
- bridge nodes from betweenness centrality when GDS is available
- untested methods/classes from coverage-gap analysis
- graph freshness confidence

## `codemeridian check-drift`

`codemeridian check-drift` asks the backend for the current drift report and exits with a non-zero code when the drift severity meets or exceeds the configured threshold.

The backend evaluates indexed file, line, and timestamp metadata. It does not read source files from the MCP server process, because the server may run in Docker or on a remote host while indexing happens from a developer machine or CI runner.

Use it when:

- you want a pre-implementation confidence check
- CI should fail if the graph looks too stale
- you want the full missing-metadata / incomplete-line / missing-timestamp report without indexing anything new

Severity thresholds:

- `--fail-on low` fails on any reported drift
- `--fail-on moderate` fails on moderate or high drift
- `--fail-on high` fails only on high drift

`codemeridian index --verify` is an alias for the same behavior, so existing indexer workflows can verify freshness without a separate command.

## Authentication and Configuration

The indexers load `.env` from the current directory and target project roots, but existing shell environment variables still win. If no environment variable is set, they fall back to project-local `meridian.json`, then the user-level global config.

Precedence for non-secret settings:

1. Explicit CLI flags such as `--project` and `--url`
2. Shell environment variables
3. Values loaded from `.env`
4. Values in project-local `meridian.json`
5. Values in global `%LOCALAPPDATA%\CodeMeridian\meridian.json`
6. Auto-detected defaults

You can generate `meridian.json` plus MCP client config with:

```powershell
codemeridian init .
```

`codemeridian init` creates or refreshes `meridian.json` from `meridian.sample.json` and merges `.vscode/mcp.json` plus `.codex/config.toml` from their sample files. If `meridian.json` already exists, rerun `codemeridian init` to merge missing defaults, bump the top-level config `version`, and keep existing project-specific values; refresh writes `meridian.json.bak` before replacing the file. The generated `meridian.json` enables `allowRepoScripts` by default so trusted repos can run repo-controlled diagnostics without an extra flag. Use `codemeridian serve` for `.env`, Docker Compose, and starting the backend stack.

You can generate a user-level fallback config with:

```powershell
codemeridian init --global --url http://localhost:5100
```

See [Global CodeMeridian Configuration](installation-global.md) for VS Code and Codex user-profile MCP registration.

Template files use the `*.sample.*` convention:

| Template | Generated file |
|----------|----------------|
| `meridian.sample.json` | `meridian.json` |
| `.env.sample` | `.env` |
| `docker-compose.sample.yaml` | `docker-compose.codemeridian.yml` |
| `.vscode/mcp.sample.json` | `.vscode/mcp.json` |
| `.codex/config.sample.toml` | `.codex/config.toml` |

Useful variables:

| Variable | Default | Description |
|----------|---------|-------------|
| `CodeMeridian_Url` | `http://localhost:5100` | CodeMeridian server URL used by indexers |
| `CodeMeridian_Project` | auto-detected | Optional project context name used when `--project` is omitted |
| `CodeMeridian_Auth_ApiKey` | empty | Optional bearer token used by the server, indexers, and MCP clients |
| `NEO4J__URI` | `bolt://codemeridian-neo4j:7687` | Neo4j URI inside Docker |
| `NEO4J__USERNAME` | `neo4j` | Neo4j username |
| `NEO4J__PASSWORD` | `CodeMeridian` | Neo4j password |
| `CODEMERIDIAN_PORT` | `5100` | Host port for the MCP server |

`meridian.json` currently supports:

| Key | Description |
|-----|-------------|
| `version` | Config format version used by `codemeridian init` to decide when new defaults can be merged into an existing file |
| `project` | Optional project context name used by the indexer when `--project` is omitted |
| `codeMeridianUrl` | Optional CodeMeridian server URL used when `CodeMeridian_Url` is not set |
| `allowRepoScripts` | When `true`, repo-controlled build and lint diagnostics are enabled by default |
| `useGlobalCache` | When `true`, runtime cache is stored outside the repository |
| `$schema` | Optional JSON schema reference. `codemeridian init` writes `./meridian.schema.json` |
| `analysis.staleKnowledge.skipHeuristicSourcePrefixes` | Documentation source prefixes where stale-knowledge uses explicit links only |
| `analysis.staleKnowledge.ignoredMentionTokens` | Exact tokens ignored by stale-knowledge heuristic scanning |
| `analysis.staleKnowledge.codeLikeSuffixes` | Suffixes used to decide whether single PascalCase words are likely code symbols |
| `analysis.staleKnowledge.ignoredDottedSuffixes` | Dotted suffixes treated as filenames, domains, or config names rather than code |
| `analysis.ranking.preferProductionOverTests` | Rank production nodes ahead of test/namespaces/boilerplate in tool output |
| `analysis.ranking.productionOnlyByDefault` | Keep broader heuristic matches hidden unless explicitly requested |
| `analysis.ranking.includeBroaderHeuristicMatches` | Show broader heuristic matches inline instead of only summarizing them |
| `analysis.ranking.includeSuppressedNoise` | Show suppressed test/config/generated findings inline for exploratory runs |
| `analysis.ranking.minimumActionableLineCount` | Minimum line count used for stronger actionability ranking |
| `analysis.ranking.testPathContains` | Case-insensitive substrings used to classify test nodes |
| `analysis.ranking.infrastructureNameSuffixes` | Node name suffixes treated as lower-priority infrastructure boilerplate |
| `analysis.ranking.infrastructureNames` | Exact node names treated as lower-priority infrastructure boilerplate |
| `analysis.ranking.suppressedFileRoles` / `analysis.ranking.suppressedNodeTypes` | File roles and node kinds hidden from primary low-noise output |
| `analysis.communityNoise.*` | Thresholds for natural-module suppression and extraction-candidate filtering |
| `analysis.coverageNoise.*` | Low-priority coverage-gap suppression thresholds and suffixes |
| `analysis.dependencyNoise.includeTestDependenciesByDefault` | Keep test-only downstream edges hidden unless explicitly requested |
| `analysis.similarityNoise.*` | Same-family/layer preference and minimum primary similarity thresholds |
| `analysis.duplicateNoise.*` | Primary duplicate-candidate thresholds and same-layer preference |
| `analysis.routePlanning.*` | Generic route-target preferences for contracts, application/domain, infrastructure, API, and CLI anchors |
| `analysis.responsibilitySlices.defaultServiceSuffix` | Default suffix for generated slice service names |
| `analysis.responsibilitySlices.namespaceRootOverrides` / `folderRootOverrides` | Optional per-project root overrides for responsibility-slice namespace and folder plans |
| `analysis.precisionFeedback.*` | Weights and storage path for acceptance/ignore feedback that nudges future target ranking |
| `analysis.testCommands.strategies[]` | Ordered, language- or path-aware command strategies for focused test suggestions; the best matching strategy for the candidate test files is used |
| `analysis.testCommands.baseCommand` / `singleTestTemplate` / `sameDirectoryTemplate` | Legacy flat fallback for focused test suggestions when no strategies are declared |

If an analysis section is omitted, CodeMeridian uses built-in defaults. These settings affect MCP analysis output such as stale-knowledge, coverage-gap, high-churn, hotspot, PageRank, and betweenness ranking.

Live MCP analysis now resolves `analysis.*` in this order:

1. Explicit request/tool behavior
2. Project-local indexed `meridian.json`
3. Global `%LOCALAPPDATA%\CodeMeridian\meridian.json` or `CODEMERIDIAN_CONFIG_HOME\meridian.json`
4. Runtime `codemeridian serve` configuration
5. Built-in defaults

Important split:

- Global `meridian.json` is read live by the shared MCP server and acts as the shared analysis fallback.
- Project-local `meridian.json` analysis settings are read from indexed `ConfigurationEntry` graph data, so you must re-index after changing them.
- Project-local `meridian.json` overrides global analysis settings for the same field.

When `CodeMeridian_Auth_ApiKey` is set, clients must send:

```http
Authorization: Bearer <your-api-key>
```

Swagger UI at `/swagger`, the raw OpenAPI document at `/openapi/v1.json`, and the GraphQL UI at `/graphql` are available without authentication so browser tooling can load. Authenticated REST API calls still require the API key. OpenAPI documents the REST endpoints under `/api/v1/*`; GraphQL query execution is available separately at `/graphql` and is protected by Hot Chocolate authorization using the same API key.

Important split:

- The indexer CLI can read `CodeMeridian_Auth_ApiKey` from `.env`.
- MCP clients such as VS Code and Codex usually cannot read a project `.env` automatically.
- On Windows, set `CodeMeridian_Auth_ApiKey` in User or System environment variables for the MCP client process, then restart the client.

## MCP Registration in VS Code

When this repository is opened in VS Code, `.vscode/mcp.json` registers CodeMeridian automatically:

```jsonc
{
  "servers": {
    "CodeMeridian": {
      "type": "sse",
      "url": "http://localhost:5100/sse",
      "headers": {
        "Authorization": "Bearer ${env:CodeMeridian_Auth_ApiKey}"
      }
    }
  }
}
```

To use CodeMeridian from another project, copy `.vscode/mcp.json` into that project's `.vscode/` folder and make sure the MCP server is running.

Use `codemeridian init .` when you want to create or merge this file for the current project.

## Stop or Reset

```powershell
# Stop containers, keep graph data
docker compose -f docker-compose.codemeridian.yml down

# Stop containers and wipe graph data
docker compose -f docker-compose.codemeridian.yml down -v
```
