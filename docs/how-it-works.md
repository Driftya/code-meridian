# How CodeMeridian Works

CodeMeridian turns a codebase into a persistent graph and exposes that graph to AI coding tools through MCP.

```text
Your codebase
     |
     v
[Indexer CLI] ---- language parsers ----> [Neo4j Graph]
                                           |
                                    [MCP Server :5100]
                                           |
                                  <--- SSE --- MCP Client
```

## Components

1. **Indexer CLI**
   Scans source code and documentation, extracts structural elements, and pushes nodes and relationships into CodeMeridian.

2. **Language indexers**
   The current indexers support C# with Roslyn and TypeScript/TSX with ts-morph. Future language indexers can write into the same graph model.

3. **Neo4j**
   Stores code nodes, relationships, documentation, timestamps, and optional embeddings. The graph persists across sessions and container restarts.

4. **MCP server**
   Exposes query and analytics tools over MCP so clients such as Copilot, Codex, or Claude Code can request architectural facts when needed.

5. **MCP client**
   Connects to the MCP server through a client-specific MCP configuration. The client remains the reasoning layer; CodeMeridian returns deterministic facts.

## What Goes Into the Graph

Typical nodes:

- Files
- Namespaces/modules
- Classes
- Interfaces
- Methods/functions
- Properties
- Enums
- Documentation chunks
- External concepts such as database tables, API endpoints, services, and topics

Typical relationships:

- `Contains`
- `Calls`
- `DependsOn`
- `Inherits`
- `Implements`
- `Uses`
- `Reads`
- `Writes`
- `PublishesTo`
- `SubscribesTo`

## Why a Graph Helps AI Tools

AI coding tools often waste context on unrelated files. CodeMeridian lets the assistant ask for the smallest useful subgraph:

- Direct callers
- Direct callees
- Implemented interfaces
- Related tests
- Recently changed nodes
- High-risk hotspots
- External systems linked to the code

That turns context selection from "paste files and hope" into targeted graph retrieval.

## Persistence

All graph data is stored in Neo4j. Closing VS Code or restarting Docker does not erase the code graph unless you explicitly clear it.

Use `clear_project_knowledge` or `codemeridian index --clear` when you want to wipe and rebuild a project's graph. Prefer `--clear` for normal re-indexing so moved or renamed files do not leave stale graph nodes behind.

Use `clear_code_graph` or `codemeridian clear --all-code-graph` for a hard reset of all indexed `CodeNode` graph data across every project. This preserves documentation knowledge.
