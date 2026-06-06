# Plan: `codemeridian serve`

## Goal

Add a `codemeridian serve` command to the .NET global tool that boots and manages a local CodeMeridian backend stack with as little manual editing as possible.

The existing `codemeridian init` command already owns `meridian.json` generation, so this work should not introduce a second init path for project-local config.

Status: implemented in `tools/Indexer` as `codemeridian serve`. The command writes or merges local config and runs `docker compose -f <compose-file> up -d` unless `--no-start` is provided.

The command should create or merge:

- `.vscode/mcp.json`
- `.codex/config.toml`
- a Docker Compose file for Neo4j and the MCP server if the user wants an auto-generated stack file
- optional `.env` defaults needed by the generated Compose file

The command should use the published GitHub Container Registry image for the MCP server instead of requiring a local source checkout build, and should wrap `docker compose` so the user can start the stack with one command.

## Proposed Command Shape

```powershell
codemeridian serve
codemeridian serve --host localhost --port 5100
codemeridian serve --image ghcr.io/driftya/codemeridian-mcp:latest
codemeridian serve --compose-file docker-compose.codemeridian.yml
codemeridian serve --force
codemeridian serve --no-start
```

Default assumptions:

- host: `localhost`
- MCP port: `5100`
- Neo4j HTTP port: `47474`
- Neo4j Bolt port: `47687`
- Compose file: `docker-compose.codemeridian.yml`
- MCP image: `ghcr.io/driftya/codemeridian-mcp:latest`
- auth env var: `CodeMeridian_Auth_ApiKey`

## Docker Compose Strategy

The generated or managed Compose file should include:

- `neo4j` using `neo4j:5.20`
- `codemeridian-mcp` using the GHCR MCP image
- named volumes for Neo4j data and logs
- health check on Neo4j
- `depends_on` from MCP to Neo4j health
- backend env vars for Neo4j connection, auth, and embeddings

The MCP service should not require a local build context.

Example image reference:

```yaml
codemeridian-mcp:
  image: ghcr.io/driftya/codemeridian-mcp:latest
```

If the repository owner or image name changes later, keep the image configurable via `--image`.

## `.env` Handling

The command may create `.env` only if it does not exist.

Recommended generated values:

```dotenv
CODEMERIDIAN_PORT=5100
NEO4J_HTTP_PORT=47474
NEO4J_BOLT_PORT=47687
NEO4J_PASSWORD=CodeMeridian
CodeMeridian_Auth_ApiKey=<generated-random-token>
Embedding__Enabled=false
Embedding__Provider=Ollama
```

If `.env` already exists:

- merge missing keys only
- do not overwrite existing values unless `--force` is set
- never print the generated API key after initial creation
- never copy the API key into `.vscode/mcp.json` or `.codex/config.toml`

## `.vscode/mcp.json` Merge Strategy

The generated MCP entry should be:

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

Implementation notes:

- Treat the file as JSONC because VS Code examples often contain comments.
- Preserve unrelated MCP servers.
- Create `servers` if missing.
- Replace only `servers.CodeMeridian` unless `--no-overwrite-client-config` is added later.
- Prefer env-var substitution over literal bearer tokens.

## `.codex/config.toml` Merge Strategy

The generated Codex config should be:

```toml
[mcp_servers.CodeMeridian]
url = "http://localhost:5100/sse"
default_tools_approval_mode = "auto"
startup_timeout_sec = 15
tool_timeout_sec = 60
bearer_token_env_var = "CodeMeridian_Auth_ApiKey"
```

Implementation notes:

- Preserve existing unrelated TOML sections.
- Replace only `[mcp_servers.CodeMeridian]`.
- Prefer a TOML parser such as `Tomlyn` if adding a dependency is acceptable.
- If avoiding a dependency, use a small section-aware merge that only removes/replaces the exact CodeMeridian section.

## Safety Rules

- Do not overwrite existing Compose, MCP, Codex, or `.env` files without a merge or `--force`.
- Print a summary of changed files and whether each was created, merged, skipped, or overwritten.
- Back up overwritten files with a timestamp suffix when `--force` is used.
- Do not write literal secrets into `.vscode/mcp.json` or `.codex/config.toml`.
- Generate API keys with cryptographic randomness.

## Suggested Implementation Steps

1. Add `serve` as a top-level command in `tools/Indexer/Program.cs`.
2. Reuse the existing `codemeridian init` flow for `meridian.json` instead of introducing a new init command.
3. Add `serve` argument parsing with defaults and `--force`.
4. Create a small `ServeOptions` model in `tools/Indexer/Cli`.
5. Create a `ServeWriter` service responsible for file creation, merge operations, and compose orchestration.
6. Add merge helpers for `.env`, JSONC MCP config, TOML Codex config, and Compose YAML.
7. Add focused unit tests in `CodeMeridian.Indexer.Tests`.
8. Document the command in `tools/Indexer/README.md`, `docs/installation.md`, and `docs/indexing.md`.

## Test Coverage

Unit tests should cover:

- creating all files in an empty directory
- merging `.vscode/mcp.json` while preserving an unrelated MCP server
- replacing an existing `CodeMeridian` MCP entry
- merging `.codex/config.toml` while preserving unrelated sections
- creating `.env` with a generated auth key
- preserving existing `.env` values
- honoring `--force` with backup files

Manual smoke test:

```powershell
codemeridian serve --compose-file docker-compose.codemeridian.yml
docker compose -f docker-compose.codemeridian.yml up -d
codemeridian doctor --project CodeMeridian
```

## Open Questions

- Default image decision: use `ghcr.io/driftya/codemeridian-mcp:latest`, matching the current publish workflow image name.
- Compose file decision: default to `docker-compose.codemeridian.yml` to avoid taking ownership of app-specific Compose files.
- Wrapper decision: `codemeridian serve` owns the wrapper behavior directly. No `server up` alias was added.
- Init decision: `codemeridian init` owns only `meridian.json`. `serve` does not call it implicitly.

## Recommendation

Start with `docker-compose.codemeridian.yml` as the default output file. It avoids taking ownership of a user's existing app Compose file and keeps the generated backend setup clearly scoped to CodeMeridian.

Use env-var based auth in client configs and generate the actual secret only in `.env`. This keeps generated project config shareable while preserving authenticated server startup.
