# Ubuntu Headless Deployment

This guide runs Neo4j and the CodeMeridian MCP server on a headless Ubuntu server, then uses the C# or TypeScript indexer from this repository on your workstation.

Example server IP:

```text
192.168.10.70
```

## What Runs Where

```text
Ubuntu server 192.168.10.70
  - Neo4j container
  - CodeMeridian MCP server container

Your workstation
  - This repository
  - Unified Indexer: tools/Indexer
  - C# Roslyn Indexer: tools/RoslynIndexer
  - TypeScript Indexer: tools/TsIndexer
  - VS Code / Codex / Copilot MCP client
```

## Prerequisites

This guide assumes Docker and the Docker Compose plugin are already installed on the Ubuntu server.

Verify:

```bash
docker --version
docker compose version
```

## Copy CodeMeridian To The Server

Clone or copy this repository onto the Ubuntu server:

```bash
git clone <your-repo-url> ~/code-meridian
cd ~/code-meridian
cp .env.example .env
```

Edit `.env`:

```bash
nano .env
```

Recommended server values:

```env
NEO4J_PASSWORD=replace-with-a-strong-password
NEO4J_HTTP_PORT=47474
NEO4J_BOLT_PORT=47687
CodeMeridian_PORT=5100
CodeMeridian_Url=http://192.168.10.70:5100
CodeMeridian_Auth_ApiKey=replace-with-a-long-random-token
```

Generate a token if needed:

```bash
openssl rand -hex 32
```

## Start The Server

From the repository root on Ubuntu:

```bash
docker compose up -d --build
docker compose ps
```

Check the MCP server health endpoint:

```bash
curl http://localhost:5100/health
```

From another machine on the same network:

```bash
curl http://192.168.10.70:5100/health
```

If `CodeMeridian_Auth_ApiKey` is set, all non-health endpoints require auth:

```bash
curl -H "Authorization: Bearer replace-with-a-long-random-token" \
  http://192.168.10.70:5100/sse
```

## Firewall With Persistent iptables

This guide assumes SSH, base firewall policy, and persistent iptables are already configured.

Allow the MCP server from the LAN:

```bash
sudo iptables -A INPUT -p tcp -s 192.168.10.0/24 --dport 5100 -j ACCEPT
```

Allow Neo4j Browser and Bolt only from the LAN:

```bash
sudo iptables -A INPUT -p tcp -s 192.168.10.0/24 --dport 47474 -j ACCEPT
sudo iptables -A INPUT -p tcp -s 192.168.10.0/24 --dport 47687 -j ACCEPT
```

Save the rules:

```bash
sudo netfilter-persistent save
sudo netfilter-persistent reload
```

Review rules:

```bash
sudo iptables -S
```

If this server is reachable from the public internet, put the MCP server behind HTTPS and a reverse proxy or VPN. Do not expose Neo4j directly to the internet.

## Use The Indexer

On your workstation, install the `codemeridian` indexer tool or run it from this repository. Put the same API key in your local `.env`:

```env
CodeMeridian_Url=http://192.168.10.70:5100
CodeMeridian_Auth_ApiKey=replace-with-a-long-random-token
```

The indexer automatically reads `.env`, so you do not need to pass the server URL or export the auth variable manually.

Index a C# or TypeScript / TSX project into the remote server:

```powershell
codemeridian index C:\Projects\MyApi
```

Watch a project:

```powershell
codemeridian index C:\Projects\MyApi --watch
```

From a source checkout, use:

```powershell
dotnet run --project tools/Indexer -- C:\Projects\MyApi
```

See [Installation](installation.md) and [Indexing Projects](indexing.md) for package installation and CLI options.

## Configure MCP Clients

For VS Code, use this in the client project's `.vscode/mcp.json`:

```jsonc
{
  "servers": {
    "CodeMeridian": {
      "type": "sse",
      "url": "http://192.168.10.70:5100/sse",
      "headers": {
        "Authorization": "Bearer ${env:CodeMeridian_Auth_ApiKey}"
      }
    }
  }
}
```

For Codex, use this in `.codex/config.toml`:

```toml
[mcp_servers.CodeMeridian]
url = "http://192.168.10.70:5100/sse"
bearer_token_env_var = "CodeMeridian_Auth_ApiKey"
default_tools_approval_mode = "auto"
startup_timeout_sec = 15
tool_timeout_sec = 60
```

Make sure `CodeMeridian_Auth_ApiKey` is available in the environment of the MCP client.

## Operations

Update and rebuild:

```bash
cd ~/code-meridian
git pull
docker compose up -d --build
```

View logs:

```bash
docker compose logs -f codemeridian-mcp
docker compose logs -f neo4j
```

Stop services:

```bash
docker compose down
```

Stop and delete graph data:

```bash
docker compose down -v
```
