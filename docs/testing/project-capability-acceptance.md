# Project Capability Acceptance

The [delivery checklist](../plans/2026-10-19-project-capability-completion-checklist.md) separates producer audits, query compatibility, and release acceptance. Passing a parser process does not establish complete semantic coverage.

## Synthetic Repository Matrix (E01)

Build the CLI and both PowerShell workers first, with the existing Node worker test dependencies installed explicitly. The harness refuses missing dependencies before invoking the CLI. It creates temporary repositories and an authenticated loopback HTTP fixture; it never contacts a configured production server.

```text
dotnet build tools/Indexer
python tools/Indexer/tests/capability_matrix_smoke.py tools/Indexer/bin/Debug/net10.0/CodeMeridian.Indexer.dll
dotnet test tests/CodeMeridian.Indexer.Tests --filter FullyQualifiedName~ProjectCapabilityMatrixTests
```

The 15 CLI cases cover C# only; TypeScript without tsconfig; Markdown only; Markdown/PowerShell; SQL disabled/enabled; mixed C#/TypeScript/frontend/document roots; unsupported and empty repositories; parse failure; skipped indexing; exclusions; failed/stale cache retention; missing roots; and inaccessible roots. Windows locks a synthetic directory exclusively, without changing ACLs. Linux temporarily removes directory permissions and requires an unprivileged user. Cleanup restores access in both cases.

Fourteen matching routing/direct-query fixtures consume discovered profiles and explicit terminal outcomes. Only complete source-bound document outcomes authorize document queries. Process-success-only code outcomes remain unknown, and disabled, partial, failed, or stale evidence blocks execution. The HTTP ingestion fixture does not emulate Neo4j queries, replay/concurrency, or ownership transactions.

## PowerShell Packaging And Generic Query Boundaries (D09/D10)

```text
python tools/Indexer/tests/worker_package_gate.py
python tools/PowerShellIndexer/tests/packaged_cli_smoke.py <installed-or-self-contained-cli-apphost>
dotnet test tests/CodeMeridian.Indexer.Tests --filter FullyQualifiedName~PowerShellPublicationTests
dotnet test tests/CodeMeridian.Application.Tests --filter FullyQualifiedName~ConnectionCapabilityBoundaryTests
```

The package gate checks complete staging and each of 18 missing apphost/parser/runtime-file cases without building or restoring packages. Actual worker/client smokes remain necessary to detect corrupt or incompatible assets. The CLI smoke uses empty PATH and synthetic sources, verifies full rebinding/deletion/failure/cache behavior, and checks non-execution. A self-contained CLI smoke and a NuGet-installed tool smoke are distinct acceptance results.

Native publication-to-query fixtures admit only explicit-file exact lookup through `powershell_definitions`, with verified full publication, source/worker identities and persisted generation. They also verify PowerShell methods and snippets cannot borrow complete C# readiness. Connection results require current inventory evidence for every intermediate node and a supported node/edge family. Impact/minimal-context results check their related nodes and snippets too. Source-less frontend concepts remain unverified. No generic PowerShell dependency or SQL/frontend compatibility is implied.

## Isolated Neo4j (E02)

[The isolated persistence workflow](../../.github/workflows/project-capability-acceptance.yml) creates an ephemeral Neo4j Community service, supplies explicit connection settings, and runs profile/outcome, constraint/reference, PowerShell publication/query, and scoped-document fixtures together. It uploads the test report even after failures. The service has no mounted persistent data and cannot select repository `.env` credentials because explicit environment settings take precedence.

To reproduce, provision a dedicated disposable Neo4j instance, explicitly set `NEO4J__URI`, `NEO4J__USERNAME`, and `NEO4J__PASSWORD`, then run:

```text
dotnet test tests/CodeMeridian.Infrastructure.Integration.Tests --filter "FullyQualifiedName~Neo4jProjectProfileRepositoryTests|FullyQualifiedName~Neo4jProjectAnalyzerOutcomeTests|FullyQualifiedName~Neo4jRepositoryConstraintSnapshotTests|FullyQualifiedName~Neo4jPowerShellPublicationTests|FullyQualifiedName~ScopedFullTextFiltersBeforeLimitAndKeepsExactFileChunks"
```

Compilation or a shared-server run does not close E02. On October 29, all five fixtures passed together against a fresh portable Neo4j Community 5.26.0 instance, with workspace-local Java 21, bound only to Windows loopback port 17687. This closes E02. The extended PowerShell fixture proves failed-source recovery, collision rollback, retry, manual preservation and persisted exact/context/impact/connection facts. TRX files are under `.artifacts/capability-acceptance/neo4j-results/`. No remote CI result is claimed.

## Fresh Server And Installed Client (E03 Progress)

With a dedicated loopback Neo4j instance and explicit `NEO4J__USERNAME`/`NEO4J__PASSWORD`, run:

```text
dotnet build src/McpServer
python tools/Indexer/tests/fresh_process_client_smoke.py src/McpServer/bin/Debug/net10.0/CodeMeridian.McpServer.dll <installed-codemeridian> bolt://127.0.0.1:<isolated-port>
```

The harness refuses non-loopback database hosts, creates synthetic sources and an isolated config home, launches and restarts the actual server, and invokes the installed SDK-based CLI. It verifies API authentication, real profile/outcome/PowerShell/document/policy publication, bounded 69-tool MCP schema discovery, scoped search, constraint bounds, PowerShell exact lookup and editing-context refusal, legacy 1.0 upload compatibility, restart persistence and stale reservations. Embeddings use the deterministic Stub provider; this does not certify production embedding completeness. Windows can publish bound filesystem-case evidence; Linux correctly retains incomplete repository constraints where case discovery is unaudited.

Windows and WSL Linux passed both this real-server smoke and the empty-PATH NuGet-installed CLI/cache smoke on October 29. Linux used a temporary local .NET SDK/Java runtime and its own Neo4j/server processes; no global PowerShell was needed. The isolated CI workflow includes the real-server smoke. E03 remains open for the full language/package/version/cancellation rollout matrix.
