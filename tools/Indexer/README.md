# CodeMeridian Indexer

`CodeMeridian.Indexer` is the unified CLI for indexing code into CodeMeridian from C#, TypeScript/TSX, and documentation sources.

## Install

```powershell
dotnet tool install -g CodeMeridian.Indexer
```

## Use

The indexer sends data to a running CodeMeridian backend. Start the MCP server and Neo4j first, usually with Docker Compose from the CodeMeridian repository:

```powershell
docker compose up -d
```

```powershell
codemeridian index .
codemeridian index C:\Projects\MyApp --project MyApp --clear
codemeridian index . --skip-csharp --skip-docs --skip-diagnostics
codemeridian index . --watch
codemeridian init .
codemeridian doctor --project CodeMeridian
codemeridian check-drift --project CodeMeridian --fail-on high
```

## What It Does

- Detects C# projects, TypeScript/TSX roots, and documentation files.
- Indexes code into Neo4j through CodeMeridian.
- Skips unchanged files after the first successful run using `.meridian/cache`.
- Can run compiler, TypeScript, and lint diagnostics unless you skip them.
- Repo-controlled build and lint diagnostics are opt-in via `--allow-repo-scripts`.
- Can query the backend for a `doctor` status report without talking to Neo4j directly from the client.
- Can verify graph drift with `codemeridian check-drift` or `codemeridian index --verify`.
- Supports dry runs and capability listing for environment checks.
- Can generate a local `meridian.json` with an auto-detected project name.
- Can also read `CodeMeridian_Project` from `.env` when you want a fixed project name without `--project`.

## Package Contents

- `LICENSE`
- This readme
- The bundled TypeScript indexer assets

## Notes

- Use `--project <name>` when you want a stable project context.
- Use `CodeMeridian_Project` in `.env` when you want the same project context applied automatically.
- Use `codemeridian doctor --project <name>` to ask the backend for graph health, drift, and counts.
- Use `codemeridian check-drift --project <name> --fail-on high` for a drift gate that exits non-zero in CI when the graph is too stale.
- Use `codemeridian index --verify --project <name>` when you want the same drift gate as part of an indexer workflow.
- Use `--skip-diagnostics` if you only want structural indexing.
- Use `--allow-repo-scripts` only on trusted repos when you want `dotnet build` and repo lint commands to run.
- Use `--no-incremental` or `--force-full` to scan all files without clearing the project.
- Use `codemeridian init .` to generate `meridian.json` when you want a project-local config file.
- The global tool does not include Neo4j or the MCP server. It requires a reachable CodeMeridian backend configured through `.env`, environment variables, or `meridian.json`.
- The repo-level README covers the full CodeMeridian product and architecture.
