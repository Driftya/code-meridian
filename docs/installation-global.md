# Global CodeMeridian Configuration

Use a global CodeMeridian config when you want the `codemeridian` CLI to work across many repositories without copying `meridian.json` into each project.

Recommended split:

- one dedicated runtime folder where you run `codemeridian serve`
- `codemeridian init .` inside repos that need project-local config
- `codemeridian init --global` once when you want user-wide fallback config

## Create The Global Config

```powershell
codemeridian init --global --url http://localhost:5100
```

On Windows this writes:

```text
%LOCALAPPDATA%\CodeMeridian\meridian.json
%LOCALAPPDATA%\CodeMeridian\meridian.schema.json
```

The global config intentionally leaves `project` empty. Project names are still auto-detected from the repository folder unless you override them with:

- `--project <name>`
- `CodeMeridian_Project` in `.env` or the shell environment
- a project-local `meridian.json`

## Precedence

CodeMeridian resolves non-secret settings in this order:

1. CLI flags, for example `--project` or `--url`
2. Shell environment variables
3. Values loaded from `.env`
4. Project-local `meridian.json`
5. Global `%LOCALAPPDATA%\CodeMeridian\meridian.json`
6. Auto-detected defaults

Project-local config always wins over global config.

For live MCP analysis behavior, the shared server reads global `analysis.*` directly from this global `meridian.json`, then overlays project-local indexed `meridian.json` analysis settings for the requested project. After changing a repo-local `analysis` section, re-index that project so the graph-backed override is refreshed.

## VS Code User MCP Registration

For a single VS Code user-profile registration, add the CodeMeridian MCP server to your user MCP configuration.

In VS Code, prefer the command palette if available:

```text
MCP: Open User Configuration
```

On Windows, the user MCP config is commonly:

```text
%APPDATA%\Code\User\mcp.json
```

Add:

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

If you do not use `CodeMeridian_Auth_ApiKey`, the header can remain present with an empty environment variable, or you can remove the `headers` block.

On Windows, VS Code needs `CodeMeridian_Auth_ApiKey` in the actual VS Code process environment. A project `.env` is not enough for this user-level MCP registration. Set the variable in User or System environment variables and restart VS Code.

## Codex Global MCP Registration

For Codex, add the server to your user-level config:

```toml
[mcp_servers.CodeMeridian]
url = "http://localhost:5100/sse"
default_tools_approval_mode = "auto"
startup_timeout_sec = 15
tool_timeout_sec = 60
bearer_token_env_var = "CodeMeridian_Auth_ApiKey"
```

On Windows this is typically:

```text
%USERPROFILE%\.codex\config.toml
```

On Windows, Codex also needs `CodeMeridian_Auth_ApiKey` in the actual Codex process environment. A repo `.env` does not automatically flow into the MCP client. Set the variable in User or System environment variables and restart Codex or the terminal that launched it.

## Project-Local Still Works

Use project-local init when a repository needs a pinned project name or a different CodeMeridian server:

```powershell
codemeridian init . --project MyApi --url http://localhost:5100
```

That repository's `meridian.json` takes precedence over the global fallback.
