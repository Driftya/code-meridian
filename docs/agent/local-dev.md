# Local Dev And Operations

## Common Commands

```powershell
# Start Neo4j and the MCP server
docker compose up -d

# Index this repo
dotnet run --project tools/Indexer -- . --project CodeMeridian

# Rebuild keyword graph
dotnet run --project tools/Indexer -- keywords rebuild --project CodeMeridian

# Classify existing keywords
dotnet run --project tools/Indexer -- keywords classify --project CodeMeridian

# Run tests
dotnet test

# Rebuild the MCP server container
docker compose up -d --build codemeridian-mcp
```

## Environment Variables

| Variable | Default | Description |
|---|---|---|
| `NEO4J__URI` | `bolt://codemeridian-neo4j:7687` | Neo4j bolt URI |
| `NEO4J__USERNAME` | `neo4j` | Neo4j username |
| `NEO4J__PASSWORD` | `CodeMeridian` | Neo4j password |
| `CODEMERIDIAN_PORT` | `5100` | MCP server host port |

Copy `.env.sample` to `.env` to override defaults.

## Using CodeMeridian On This Repo

Once the backend is running and the repo is indexed, useful prompts include:

```text
What calls Neo4jCodeGraphRepository.UpsertNodeAsync?
What is the architectural overview of CodeMeridian?
Which methods have no test coverage?
What changed in the last 7 days?
```

## Commit Messages

Commit messages are enforced by `.githooks/commit-msg`.
