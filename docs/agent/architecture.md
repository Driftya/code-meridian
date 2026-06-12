# Architecture Rules

CodeMeridian follows Clean Architecture. Dependencies point inward.

## Layers

```text
McpServer
  -> Application
  -> Core
  -> Infrastructure via DI

Application
  -> Core

Infrastructure
  -> Core

Core
  -> nothing

Sdk
  -> standalone client
```

## Dependency Rules

| Layer | May depend on | Must not depend on |
|---|---|---|
| `Core` | nothing | anything else |
| `Application` | `Core` | `Infrastructure`, `McpServer` |
| `Infrastructure` | `Core` | `Application`, `McpServer` |
| `McpServer` | `Application`, `Core`, `Infrastructure` via DI | direct architecture violations |
| `Sdk` | standalone HTTP concerns | repository internals |

If a `Core` type gains a dependency on `Neo4j.Driver`, `Microsoft.EntityFrameworkCore`, or similar infrastructure packages, treat it as a violation.

## Key Files

| File | Purpose |
|---|---|
| `src/Core/CodeGraph/CodeNode.cs` | Domain model for code nodes |
| `src/Core/CodeGraph/CodeEdge.cs` | Domain model for graph edges |
| `src/Core/CodeGraph/ICodeGraphRepository.cs` | Graph repository contract |
| `src/Infrastructure/Graph/Neo4jCodeGraphRepository.cs` | Neo4j Cypher implementation |
| `src/Application/Services/CodebaseQueryService.cs` | Formats graph results for tools |
| `src/McpServer/Tools/CodebaseTools.cs` | MCP graph/query tools |
| `src/McpServer/Tools/KnowledgeTools.cs` | MCP ingestion tools |
| `src/McpServer/Tools/ExtensionTools.cs` | Extension agent and external concept tools |
| `src/McpServer/Program.cs` | Composition root |

## Neo4j Rules

- Consume write cursors with `await cursor.ConsumeAsync()`.
- Preserve `createdAt`; update `updatedAt` on writes.
- Put schema changes in `Neo4jInitializationService.InitializeAsync()`.
- Keep Cypher in infrastructure, not application or MCP layers.
