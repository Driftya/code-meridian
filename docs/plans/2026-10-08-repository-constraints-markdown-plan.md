# Repository Constraints And Markdown Structure

Date: 2026-10-08

Status: planned.

Depends on the inventory/profile in [plan 1](2026-10-08-project-discovery-capability-profile-plan.md); integrates with [tool routing](2026-10-08-capability-aware-tool-routing-plan.md).

## Problem And Intended Behavior

Architecture rules currently classify namespaces and check code dependencies. Repository structure also includes documents, SQL sources, scripts, images, and publication assets. A namespace-only policy cannot enforce their placement or references.

Extend project-owned architecture configuration with explicit rule families for repository placement and local Markdown references. Preserve existing namespace rules and return evaluated, inapplicable, unsupported, invalid, and incomplete rule outcomes distinctly.

## Boundaries And MVP

MVP supports:

- File groups selected by repository-relative glob patterns and optional observed file kinds.
- Allowed locations and explicit deny patterns for a group.
- Required local reference resolution and allowed target groups for Markdown links/images.
- Rule IDs, reasons, severity, explicit exceptions, and evidence paths/locations.

Defer repository-specific chapter sequencing, front-matter schemas, contiguous image numbering, semantic narrative consistency, and arbitrary custom validators. Add them only as separately specified follow-ups. The first acceptance example can validate chapter-image placement and local asset references without claiming to validate the whole novel.

SQL file placement uses inventory and does not require SQL parsing. SQL semantic dependency rules require advertised analyzer evidence and tool support; do not mistake a Reads edge for a code Calls edge.

## Configuration And Rule Semantics

Keep `architecture.path` and existing layer/dependency definitions compatible. Add a versioned schema for groups, placement rules, and reference rules; exact property names are implementation decisions to validate before rollout.

- Groups can overlap. Evaluate every applicable rule; do not arbitrarily assign a file to one layer.
- Normalize separators and dot segments without erasing case-sensitive filesystem distinctions.
- Distinguish a forbidden path from a missing file and from an excluded/unobserved target.
- Default placement scope is files admitted by inventory/exclusions. Do not silently include generated outputs or assets outside the repository.
- Rules identify their required capabilities. Invalid patterns, unknown group IDs, and unsupported rule families produce diagnostics.
- Validate configured rules before indexing/query execution. Invalid custom policy must not fall back silently to the default Clean Architecture policy.
- No automatic repository rewrite, file move, canon edit, or constraint inference from file names.
- Keep semantic authority statements such as canon constrains manuscript in agent/review guidance. A link policy cannot establish truth or narrative compliance.

Read-only `ava-sporelight` example groups could include `canon/**`, `manuscript/**/*.md`, `manuscript/images/**`, `concepts/**`, and `scripts/**/*.ps1`. These are illustrative, not a config change to that repository.

## Markdown Reference Evidence

Audit `DocumentReferenceExtractor` first: current extraction is regex-based and used for searchable document/code references, not a complete structural validator.

For validation, use a syntax-aware parser after a small compatibility spike. Record source file, source span, link/image kind, raw target, normalized target, optional fragment, resolution outcome, parser fingerprint, and inventory generation.

- Cover inline and reference-style links/images; avoid treating code blocks or escaped text as links.
- Specify fragment handling and heading-anchor rules rather than guessing renderer behavior. Report unverified fragments where semantics are unavailable.
- Decode URL paths correctly; distinguish external URLs, fragments, data URLs, and repository-relative paths.
- Define root-relative link semantics explicitly. Record out-of-repository paths without traversing them.
- Do not fetch remote URLs or load linked files outside the allowed repository boundary.
- Raw HTML/reference forms unsupported by the selected parser remain explicit unsupported evidence.
- Retain original reference text for diagnostics but keep returned snippets bounded.
- Check inventory assets without requiring embedding/image understanding.
- Parse failure or incomplete inventory yields incomplete validation, not success.

Inventory references and searchable KnowledgeDocument chunks need stable file-level identity. Do not turn every Markdown heading into a code symbol or duplicate one file per chunk.

## Existing And Proposed Surfaces

| Surface | Responsibility |
|---|---|
| `src/Core/CodeGraph/ArchitectureRuleSet.cs` | Backward-compatible parser-independent rule contracts; split new public types into files |
| `src/Infrastructure/Graph/Neo4jCodeGraphRepository.Architecture.cs` | Existing rule loading; extend validated persistence/query behavior |
| `Neo4jCodeGraphRepository.Analytics.cs` | Keep namespace query semantics; compose new rule results without mixing edge meaning |
| `tools/DocumentIndexer/Pipeline/DocumentReferenceExtractor.cs`, `DocumentIndexerPipeline.cs` | Reference fidelity audit and syntax-aware structural facts |
| Project inventory/profile contracts | File identity, existence, exclusions, completeness, and generations |
| Configuration schema/templates, Application, SDK/API/MCP | Explicit rule-family support and bounded diagnostics |
| `docs/agent/`, architecture templates | Document code-only versus repository rules and applicability |

Do not place a Markdown parser or Neo4j driver in Core. Preserve searchable documentation and existing links while adding structural evidence.

## Phases

1. Define rule semantics, schema validation, outcome types, and compatibility fixtures.
2. Implement inventory-only placement checks and a narrow public repository-constraint query.
3. Add Markdown reference parsing, generation-safe publication, and reference checks.
4. Integrate capability routing and reports; expose per-rule evaluation coverage.
5. Validate read-only examples and document additional validators as deferred work.

Proposed new tool: `find_repository_constraint_violations`, scoped by project and optionally target. Keep `find_architecture_violations` code-compatible, with report composition sharing validated outcomes.

## Verification And Acceptance

- Existing architecture configuration keeps its namespace/dependency behavior.
- Documentation-only fixtures successfully run placement/reference rules and mark namespace rules inapplicable.
- Wrongly placed Markdown, SQL, script, and asset files produce rule-specific diagnostics.
- Valid inline/reference-style links and images resolve; missing/forbidden references identify file and source span.
- Code fences, escaped syntax, URL encodings, spaces, fragments, separators, case distinctions, and traversal receive focused fixtures.
- Invalid policies never produce an all-clear report or silent default substitution.
- Excluded/missing inventory targets and parse failures expose coverage limits.
- Publication replay, changed links, file renames/deletions, and failed parsing preserve generation/failure semantics.
- Test Core validation, DocumentIndexer pipeline, Application formatting, API contracts, and isolated Neo4j persistence.
- No remote fetches and no write to `ava-sporelight` during acceptance.

## Risks

Regex extraction cannot justify complete Markdown validation. Renderer-specific anchors and raw HTML require explicit limits. Placement checks are only as current as the inventory. Preserve diagnostics even when other rules pass, and keep rule outcomes separate from semantic content review.
