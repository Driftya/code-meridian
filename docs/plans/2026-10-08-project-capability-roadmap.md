# Project Awareness And Repository Analysis Roadmap

Date: 2026-10-08

Status: in progress; plan 1 local discovery, persisted inventory, explicit CLI publication, and API/SDK/MCP queries implemented. Analyzer outcome tracking, tool routing, repository constraints, and PowerShell indexing remain planned.

## Goal

Make CodeMeridian aware of repository composition before choosing analysis tools. Support code-only, documentation-only, and mixed repositories without treating missing graph evidence as an absence of source files. Extend structural checks to repository assets and add offline PowerShell analysis.

## Plans And Delivery Order

| Order | Plan | Outcome | Dependency |
|---|---|---|---|
| 1 | [Project discovery and capability profiles](2026-10-08-project-discovery-capability-profile-plan.md) | Persist observed files, enabled analyzers, successful indexing, and capability evidence; expose a profile query | Existing discovery/indexing |
| 2 | [Capability-aware tool routing](2026-10-08-capability-aware-tool-routing-plan.md) | Select tools from task, target, and available evidence; explain unavailable analyses | Profile query from plan 1 |
| 3 | [Repository constraints and Markdown structure](2026-10-08-repository-constraints-markdown-plan.md) | Validate asset placement and local document references using project-owned rules | File inventory from plan 1; integrates with plan 2 |
| 4 | [Offline PowerShell indexing](2026-10-08-powershell-indexing-plan.md) | Parse scripts/modules and publish source-backed definitions and conservative dependencies | Profile contract from plan 1; integrates with plan 2 |

Deliver plans 1 and 2 first to fix incorrect tool selection for existing languages. Plans 3 and 4 can then progress independently, with coordinated changes to shared CLI/configuration contracts. PowerShell parser compatibility investigation can start before plan 1 finishes.

## Shared Decisions

- A repository can contain several languages and asset types. A dominant content label is display metadata, not a routing gate.
- Record detected, supported, configured, and indexed states separately.
- Capability evidence is scoped to repository, analysis root, and target where applicable.
- File discovery is independent of whether an analyzer is enabled.
- Server tools read uploaded evidence; they do not assume access to the user's local filesystem.
- A missing, stale, incomplete, or failed profile never proves that a language is absent.
- Code, document, SQL, and PowerShell relationships have different semantics; describe supported relationships precisely.
- Architecture checks are deterministic facts and diagnostics. Narrative consistency and author intent remain evidence-backed review.
- New configuration fields and tool contracts need explicit versioning and compatibility behavior.
- Core holds parser-independent contracts. Parser packages stay in tooling; Cypher stays in Infrastructure.

## Shared Acceptance Matrix

| Repository fixture | Expected behavior |
|---|---|
| C# only | C# discovery and supported symbol/dependency workflows |
| TypeScript only | TypeScript discovery; generic code workflows where indexed evidence exists |
| Markdown only | Document search; structural checks when implemented; code symbol workflows inapplicable |
| Markdown plus PowerShell | Document tasks use document evidence; script tasks use PowerShell evidence after indexing |
| SQL disabled but files present | SQL detected; indexing disabled; no fabricated dependencies |
| SQL enabled and indexed | Dependency workflows limited to supported indexed SQL relationships |
| Mixed monorepo | A chapter target does not inherit C# capabilities from an unrelated application root |
| Unsupported language present | Files remain visible; analyzer support is explicitly unavailable |
| Failed, skipped, or stale indexing | Report incomplete evidence rather than a clean analysis result |

Use synthetic fixtures for automated tests. Use `ava-sporelight` as an explicit read-only acceptance example, not a unit-test filesystem dependency. Do not rewrite its configuration, canon, or scripts as part of implementing these plans.

## Evidence And Limits

Existing discovery is in `src/Tooling/Discovery/ProjectDiscoveryService.cs`; indexing dispatch is in `tools/Indexer/Cli/IndexCommandHandler.cs`. Existing workflow planning uses a deterministic tool catalog and recipes. Existing architecture rules classify namespaces. Existing document indexing extracts links but needs a fidelity audit before claiming complete Markdown reference validation.

CodeMeridian feature mapping and minimal-context tools were used. Discovery/planner nodes have high-confidence indexed metadata; relationship completeness is medium, with the latest reported incremental index on 2026-10-06. Some broad feature-map hits were unrelated, particularly for PowerShell, and are not evidence of implemented support. Local source reads narrowed the seams.

No runtime tests are required for creating these plans. Each implementation plan specifies its own verification gates.
