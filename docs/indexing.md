# Indexing Projects

The unified indexer CLI scans a target directory and runs the available language indexers. It currently supports C#, TypeScript/TSX, and documentation ingestion.

## Start the Server

```powershell
Copy-Item .env.example .env
docker compose up -d
```

This starts:

- Neo4j browser: `http://localhost:47474`
- Neo4j bolt: `bolt://localhost:47687`
- MCP server: `http://localhost:5100/sse`

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
codemeridian index . --dry-run
codemeridian index . --clear
codemeridian clear --project CodeMeridian
```

## Source Checkout Usage

You can also run the unified indexer directly from this repository:

```powershell
dotnet run --project tools/Indexer -- . --clear
```

## Common Commands

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
| `--no-incremental` / `--force-full` | Ignore the local file cache and scan all enabled files |
| `--watch` | Stay running and re-index when files change |

## Clear Commands

Use `codemeridian index --clear` for normal rebuilds. It clears the selected project once before indexing, which avoids stale nodes after file moves, renames, or indexer ID changes.

Use `codemeridian clear --project <name>` when you want to remove one project's indexed code graph and documentation without immediately re-indexing.

Use `codemeridian clear --all-code-graph` only for a hard reset of indexed code nodes across every project. It removes `CodeNode` nodes and relationships but preserves documentation nodes.

## Incremental Indexing

By default, the unified indexer stores a language-neutral file snapshot in `.meridian/cache`. Later runs compare file path, last-write time, and length so unchanged files can be skipped before invoking the C# or TypeScript indexer.

Changed and deleted files are removed from the project graph before re-indexing, which prevents old symbols from lingering when a file is edited or removed. Use `--clear` after major renames, project moves, or indexer ID changes. Use `--no-incremental` or `--force-full` when you want a full scan without clearing existing knowledge.

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

## Authentication and Configuration

The indexers read `.env` from the current directory or a parent directory first. If no environment variable is set, they fall back to `meridian.json` in the target root or a parent directory.

Precedence for non-secret settings:

1. Explicit CLI flags such as `--project` and `--url`
2. Shell environment variables
3. Values loaded from `.env`
4. Values in `meridian.json`
5. Auto-detected defaults

You can generate `meridian.json` with:

```powershell
codemeridian init .
```

Useful variables:

| Variable | Default | Description |
|----------|---------|-------------|
| `CodeMeridian_Url` | `http://localhost:5100` | CodeMeridian server URL used by indexers |
| `CodeMeridian_Project` | auto-detected | Optional project context name used when `--project` is omitted |
| `CodeMeridian_Auth_ApiKey` | empty | Optional bearer token sent by indexers and MCP clients |
| `NEO4J__URI` | `bolt://codemeridian-neo4j:7687` | Neo4j URI inside Docker |
| `NEO4J__USERNAME` | `neo4j` | Neo4j username |
| `NEO4J__PASSWORD` | `CodeMeridian` | Neo4j password |
| `CODEMERIDIAN_PORT` | `5100` | Host port for the MCP server |

`meridian.json` currently supports:

| Key | Description |
|-----|-------------|
| `project` | Optional project context name used by the indexer when `--project` is omitted |
| `codeMeridianUrl` | Optional CodeMeridian server URL used when `CodeMeridian_Url` is not set |

When `CodeMeridian_Auth_ApiKey` is set, clients must send:

```http
Authorization: Bearer <your-api-key>
```

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

## Stop or Reset

```powershell
# Stop containers, keep graph data
docker compose down

# Stop containers and wipe graph data
docker compose down -v
```
