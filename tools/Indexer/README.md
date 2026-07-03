# CodeMeridian Indexer
[![MCP Image](https://img.shields.io/github/v/tag/Driftya/code-meridian?label=MCP%20Image&sort=semver)](https://github.com/Driftya/code-meridian/pkgs/container/codemeridian-mcp)
[![CI](https://github.com/Driftya/code-meridian/actions/workflows/ci.yml/badge.svg)](https://github.com/Driftya/code-meridian/actions/workflows/ci.yml)

`CodeMeridian.Indexer` is the unified CLI for indexing code into CodeMeridian from C#, TypeScript/TSX, documentation, and configuration sources.

## Install

```powershell
dotnet tool install -g CodeMeridian.Indexer
```

## Use

The indexer sends data to a running CodeMeridian backend. Run `codemeridian serve` in a dedicated runtime folder for the shared MCP server and Neo4j stack, then run `codemeridian init .` in each indexed project or `codemeridian init --global` for user-wide defaults.

To create local runtime files and start Neo4j plus the MCP server with Docker Compose:

```powershell
codemeridian serve --no-start
codemeridian serve
```

```powershell
codemeridian init .
codemeridian index .
codemeridian index C:\Projects\MyApp --project MyApp --clear
codemeridian index .
codemeridian index . --skip-keywords
codemeridian keywords --project MyApp
codemeridian keywords rebuild --project MyApp
codemeridian keywords index --project MyApp
codemeridian keywords classify --project MyApp
codemeridian keywords rebuild --project MyApp
codemeridian keywords status --job-id 11111111-2222-3333-4444-555555555555
codemeridian config rebuild --project MyApp
codemeridian index . --skip-csharp --skip-docs --skip-diagnostics
codemeridian index . --skip-config
codemeridian index . --watch

codemeridian doctor --project CodeMeridian
codemeridian report --project CodeMeridian
codemeridian report pr-context --project CodeMeridian --base origin/main --head HEAD --format markdown --output artifacts/pr-context.md
codemeridian check-drift --project CodeMeridian --fail-on high
codemeridian evaluate-session . --project MyApp --session .meridian/sessions/session.jsonl
```

## What It Does

- Detects C# projects, TypeScript/TSX roots, and documentation files.
- Detects repo-local configuration files such as `appsettings*.json`, `meridian*.json`, `.env`, and Docker Compose YAML.
- Indexes code into Neo4j through CodeMeridian.
- Indexes canonical configuration keys and links direct and typed C# configuration usage into the same graph.
- Skips unchanged files after the first successful run using `.meridian/cache`.
- Can run compiler, TypeScript, and lint diagnostics unless you skip them.
- Rebuilds and classifies the backend keyword graph after indexing unless `--skip-keywords` is set.
- Queues incremental keyword refresh automatically as nodes and documents are ingested by the MCP server.
- Can rebuild the keyword graph on demand without indexing through `codemeridian keywords`. Rebuild also runs keyword classification after the rebuild finishes.
- Can classify already-built keywords on demand through `codemeridian keywords classify`.
- Can rebuild the configuration graph on demand without rerunning language indexers through `codemeridian config rebuild`.
- Can read `configurationFiles` from `meridian.json` to control which config file names or wildcard patterns are indexed.
- Repo-controlled build and lint diagnostics are opt-in via `--allow-repo-scripts`.
- Can query the backend for a `doctor` status report without talking to Neo4j directly from the client.
- Can print an architecture weather report with graph counts, risks, bridge nodes, coverage gaps, and freshness.
- Can generate CI-friendly PR context reports as Markdown or JSON without requiring an interactive assistant.
- Can verify graph drift with `codemeridian check-drift` or `codemeridian index --verify`.
- Can evaluate whether CodeMeridian helped an implementation session by comparing provider-neutral session evidence with git changes, then write `.meridian/precision-feedback.json` for future ranking feedback.
- Can create local runtime files and start the backend stack with `codemeridian serve`.
- Supports dry runs and capability listing for environment checks.
- Can generate a local `meridian.json` and MCP client config with an auto-detected project name.
- Can refresh an existing `meridian.json` in place by rerunning `codemeridian init .`, merging missing defaults without overwriting local settings.
- Can seed `.meridian/architecture.json` and copy bundled templates from the package `architectures/` folder into `.meridian/architectures/` so architecture checks are project-owned and editable.
- Can seed `.meridian/keyword-classification.json` from the packaged root `keyword-classification.sample.json` so keyword classification rules live in the repo instead of being hardcoded in the backend.
- Can seed `.meridian/database-tracing.json` from the packaged root `database-tracing.sample.json` so database-recognition presets stay repo-owned instead of being buried in backend code.
- Can copy bundled agent guidance into `meridian-agent-capabilities/` so repo-local agent instructions travel with the setup without writing into a user-owned `docs/` tree, including Codex-specific helper scripts under `codex-scripts/`.
- Can also read `CodeMeridian_Project` from `.env` when you want a fixed project name without `--project`.

## Package Contents

- `LICENSE`
- This readme
- The bundled TypeScript indexer assets

## Notes

- Use `--project <name>` when you want a stable project context.
- Use `CodeMeridian_Project` in `.env` when you want the same project context applied automatically.
- Use `codemeridian doctor --project <name>` to ask the backend for graph health, drift, and counts.
- Use `codemeridian report --project <name>` for a compact architecture weather report.
- Use `codemeridian trace-endpoint "POST /api/orders" --project <name>` when you want a graph-only route trace through indexed database and messaging paths. Database tracing is driven by `.meridian/database-tracing.json` and currently ships starter presets for EF Core, Dapper, raw SQL, Prisma, Knex, and Neo4j Cypher. `trace_endpoint` is an alias if you prefer the MCP-style name.
- Use `codemeridian report pr-context --base <git-ref> --head <git-ref>` when you want a deterministic PR review summary with changed graph nodes, impact hints, missing-test warnings, hotspot/churn warnings, and related docs. Add `--format json` for automation and `--output <path>` for CI artifacts.
- Use `codemeridian check-drift --project <name> --fail-on high` for a drift gate that exits non-zero in CI when the graph is too stale.
- Use `codemeridian evaluate-session --project <name>` to read the newest `.meridian/sessions/*.jsonl` evidence file, report whether CodeMeridian suggestions matched changed files and tests, and refresh `.meridian/precision-feedback.json`. Pass `--session <file-or-directory>` to choose evidence explicitly and `--base <git-ref>` to change the diff base. See [Evaluating Session Usefulness](../../docs/evaluate.md) for the step-by-step workflow.
- Use `codemeridian index --verify --project <name>` when you want the same drift gate as part of an indexer workflow.
- Use `--skip-diagnostics` if you only want structural indexing.
- Use `--skip-config` when you want to skip configuration-file parsing and config-usage graph edges for a run.
- Use `--skip-keywords` when you do not want the index run to finish by rebuilding and classifying derived keywords.
- Use `codemeridian config rebuild --project <name>` when you want a clean rebuild of configuration-file nodes, canonical config keys, and C# config usage links.
- Use `.meridian/database-tracing.json` to tune how C# indexing recognizes EF Core, Dapper, and raw SQL database access. The default presets emit `DatabaseOperation` and `DatabaseTable` graph concepts without hardcoding one data-access style into the backend.
- Use `codemeridian keywords --project <name>` as the short form when you want a rebuild plus classification. It submits a background job and returns a job id immediately.
- Use `codemeridian keywords rebuild --project <name>` when you want an explicit maintenance command name. It also submits a background job and runs classification after rebuild.
- Use `codemeridian keywords index --project <name>` if you prefer `index` terminology; it is an alias of `rebuild`.
- Use `codemeridian keywords classify --project <name>` when you only want to classify existing derived keywords without rebuilding relationships. It also submits a background job.
- Use `codemeridian keywords rebuild --project <name> --wait` or `codemeridian keywords classify --project <name> --wait` when you want to start the job in the background but keep the CLI attached until it completes.
- Use `codemeridian keywords status --job-id <id>` when you want to verify whether a background keyword job finished, failed, or expired.
- Use `--allow-repo-scripts` only on trusted repos when you want `dotnet build` and repo lint commands to run.
- Use `--no-incremental` or `--force-full` to scan all files without clearing the project.
- Use `codemeridian init .` to create or refresh `meridian.json` for a project and then step through prompts for `.vscode`, `.continue`, `.codex`, and `meridian-agent-capabilities`. When `meridian.json` already exists, `init` merges missing defaults, bumps the config `version`, and writes `meridian.json.bak` before replacing the file. The generated `meridian.json` enables `allowRepoScripts` by default for trusted repos.
- Use `codemeridian init --global --url http://localhost:5100` to create a user-level fallback config when you want the CLI to work across many repos without project-local config. Global init also seeds `.meridian/architecture.json`, `.meridian/keyword-classification.json`, `.meridian/database-tracing.json`, `.meridian/architectures/`, and `meridian-agent-capabilities/` under the global config root.
- Use `codemeridian serve` in a separate runtime folder to create `.env` and `docker-compose.codemeridian.yml`, then run `docker compose pull` followed by `docker compose up -d`.
- Use `codemeridian serve --no-start` when you only want to write or merge those runtime files.
- Use `codemeridian init .` when you want to create or merge client MCP config such as `.vscode/mcp.json`, `.continue/mcpServers/code-meridian.yaml`, or `.codex/config.toml`.
- On Windows, keep `CodeMeridian_Auth_ApiKey` in the runtime `.env` for the server and also in User or System environment variables for MCP clients such as VS Code or Codex; project `.env` files are not automatically imported into those client processes.
- The global tool does not include Neo4j. It starts Neo4j and the MCP server through Docker using the published MCP server image.
- The repo-level README covers the full CodeMeridian product and architecture.
