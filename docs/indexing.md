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
```

## Source Checkout Usage

You can also run the unified indexer directly from this repository:

```powershell
dotnet run --project tools/Indexer -- .
```

## Common Commands

Index the current directory:

```powershell
codemeridian index
```

Index another project:

```powershell
codemeridian index C:\Projects\MyApi
```

Set an explicit project context:

```powershell
codemeridian index C:\Projects\MyApi --project MyApi
```

Clear existing graph data before indexing:

```powershell
codemeridian index . --clear
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
| `--include-diagnostics` | Reserved for future diagnostics indexing |
| `--watch` | Stay running and re-index when files change |

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

## Authentication and Configuration

The indexers read `.env` from the current directory or a parent directory.

Useful variables:

| Variable | Default | Description |
|----------|---------|-------------|
| `CodeMeridian_Url` | `http://localhost:5100` | CodeMeridian server URL used by indexers |
| `CodeMeridian_Auth_ApiKey` | empty | Optional bearer token sent by indexers and MCP clients |
| `NEO4J__URI` | `bolt://codemeridian-neo4j:7687` | Neo4j URI inside Docker |
| `NEO4J__USERNAME` | `neo4j` | Neo4j username |
| `NEO4J__PASSWORD` | `CodeMeridian` | Neo4j password |
| `CODEMERIDIAN_PORT` | `5100` | Host port for the MCP server |

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
