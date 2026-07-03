# Installation

CodeMeridian has two pieces:

1. The CodeMeridian server stack: Neo4j plus the MCP server.
2. The `codemeridian` indexer CLI: the command that scans projects and pushes code structure into the server.

## Prerequisites

- Docker Desktop
- .NET 10 SDK
- GitHub Copilot in VS Code
- Node.js 18+ when indexing TypeScript / TSX

## Install the Indexer CLI

### From NuGet

This works only after `CodeMeridian.Indexer` is published to NuGet.org or another configured NuGet feed:

```powershell
dotnet tool install --global CodeMeridian.Indexer
```

Update it later with:

```powershell
dotnet tool update --global CodeMeridian.Indexer
```

Verify:

```powershell
codemeridian --list-capabilities
```

### From This Repository

Until the package is published, build and install it locally:

```powershell
dotnet pack tools/Indexer -o artifacts/packages
dotnet tool install CodeMeridian.Indexer --global --add-source artifacts/packages
```

If you already installed a local version:

```powershell
dotnet tool update CodeMeridian.Indexer --global --add-source artifacts/packages
```

## Start the Server

Run `codemeridian serve` in a dedicated runtime folder for the shared CodeMeridian backend, not inside every project you want to index.

Create the local server runtime files and start the containers:

```powershell
codemeridian serve
```

This creates or merges `.env` and `docker-compose.codemeridian.yml` from the repo's `*.sample.*` templates, then runs `docker compose -f docker-compose.codemeridian.yml pull` followed by `docker compose -f docker-compose.codemeridian.yml up -d`.

To generate the runtime files without starting Docker:

```powershell
codemeridian serve --no-start
```

This starts:

- Neo4j browser: `http://localhost:47474`
- Neo4j bolt: `bolt://localhost:47687`
- MCP server: `http://localhost:5100/sse`

Typical split:

- One runtime folder: run `codemeridian serve` there to manage `.env`, `docker-compose.codemeridian.yml`, Neo4j, and the MCP server.
- Each indexed project: run `codemeridian init .` in that project when you want project-local `meridian.json` and MCP client config.
- User-wide fallback: run `codemeridian init --global` when you want the indexer and MCP client defaults available across many repos.

Open this repository in VS Code. The repo's checked-in `.vscode/mcp.json` registers the MCP server for GitHub Copilot.

For source-checkout development, you can still start the repository compose file directly:

```powershell
Copy-Item .env.sample .env
docker compose up -d
```

## Index a Project

Index the current directory:

```powershell
codemeridian index . --clear
```

Index another project:

```powershell
codemeridian index C:\Projects\MyApi --project MyApi --clear
```

Preview what will be indexed:

```powershell
codemeridian index . --dry-run
```

Clear and rebuild a project graph:

```powershell
codemeridian index . --clear
```

## Source Checkout Alternative

You can run the indexer without installing the tool:

```powershell
dotnet run --project tools/Indexer -- .
```

For rebuilds from a source checkout, prefer:

```powershell
dotnet run --project tools/Indexer -- . --clear
```

The installed tool is the recommended path for regular use.

See [Publishing the Indexer Tool](publishing.md) for NuGet publishing steps.

## TypeScript / TSX Notes

The packaged indexer includes the TypeScript indexer source. On the first TypeScript indexing run, it restores its npm dependencies if needed.

Node.js 18+ must be available on `PATH`.

## Authentication

For public or shared deployments, set `CodeMeridian_Auth_ApiKey` in the runtime folder `.env` so the server container starts with auth enabled.

The indexer reads `.env` from the current directory or a parent directory first, then falls back to `meridian.json` for non-secret settings like the server URL or project name.

Important: MCP clients such as VS Code, Codex, and similar tools do not automatically read your project `.env` just because the server does. They send `Authorization: Bearer ${env:CodeMeridian_Auth_ApiKey}` from their own process environment.

On Windows, the practical setup is usually:

1. keep `CodeMeridian_Auth_ApiKey` in the runtime folder `.env` for `codemeridian serve`
2. also set `CodeMeridian_Auth_ApiKey` in your User or System environment variables for the MCP client
3. restart VS Code, Codex, or any terminal/agent process after changing the environment variable

If you want to pin the project name without using `--project`, set `CodeMeridian_Project` in `.env`:

```env
CodeMeridian_Project=MyApi
```

Generate a project-local `meridian.json` plus MCP client config with:

```powershell
codemeridian init .
```

`codemeridian init` creates or refreshes the project indexing config from `meridian.sample.json`, enables `allowRepoScripts` by default in `meridian.json`, and merges `.vscode/mcp.json` plus `.codex/config.toml` from their sample files. If `meridian.json` already exists, rerun `codemeridian init` to merge missing defaults and bump the config `version` without overwriting existing project-specific values. Use `codemeridian serve` for `.env`, Docker Compose, and starting the backend stack.

For a machine-wide fallback config, see [Global CodeMeridian Configuration](installation-global.md).

The API key sent by the indexer or MCP client is:

```http
Authorization: Bearer <your-api-key>
```

For the indexer CLI, `.env` is usually enough.

For MCP clients, prefer a real environment variable in the client process, especially on Windows.

You can also set the server URL explicitly:

```powershell
codemeridian index . --url http://localhost:5100
```

## Clear Indexed Data

Clear one project without indexing:

```powershell
codemeridian clear --project MyApi
```

Clear all indexed code graph nodes across every project while preserving documentation:

```powershell
codemeridian clear --all-code-graph
```

## Uninstall

Remove the indexer tool:

```powershell
dotnet tool uninstall --global CodeMeridian.Indexer
```

Stop the server:

```powershell
docker compose -f docker-compose.codemeridian.yml down
```

Stop the server and wipe graph data:

```powershell
docker compose -f docker-compose.codemeridian.yml down -v
```
