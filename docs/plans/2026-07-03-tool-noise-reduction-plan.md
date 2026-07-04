# Tool Noise Reduction Plan

- Status: completed
- Date: 2026-07-03
- Scope: generic noise reduction for CodeMeridian discovery, ranking, and refactor-planning tools
- Principle: prefer generic behavior changes; when repo-specific behavior is needed, express it through configuration or indexed metadata instead of hardcoded repository rules

## Success Criteria

- [ ] Broad discovery tools rank actionable production code ahead of tests, external concepts, config keys, and graph artifacts by default.
- [ ] Exploratory tools still allow broader output, but clearly separate production candidates from lower-confidence or non-code noise.
- [ ] Refactor-planning tools produce route targets and candidate slices that are materially more actionable on arbitrary indexed codebases.
- [ ] Repo-specific exceptions are handled through configuration or indexer-supplied file-role metadata instead of tool-specific hardcoding.
- [ ] Each tool changed in this plan has regression coverage for both the default low-noise behavior and the opt-in broader behavior.

## Cross-Cutting Fixes

- [x] Add a shared "actionability" ranking policy for broad graph tools.
  - Default behavior: prefer production `Class` and `Method` nodes with file-backed metadata, meaningful line counts, and non-test file roles.
  - Penalize by default: test nodes, external concepts, database tables, configuration keys, single-field/property artifacts, and synthetic helper nodes with no direct production action.
  - Configuration-first detail: expose weights and exclusions through analysis options or project config so a codebase can tune ranking without code changes.
  - Indexer update needed: yes, if we want stronger generic ranking from file-role metadata, node provenance, or richer "is synthetic/generated/config/external" flags beyond current file-role inference.

- [x] Add shared output sections for `production candidates`, `broader heuristic matches`, and `suppressed noise`.
  - Goal: keep precision high by default while making suppressed results inspectable when needed.
  - Configuration-first detail: add an opt-in flag to include suppressed sections inline or hide them entirely.
  - Indexer update needed: no for sectioning; yes only if suppression depends on new indexed node categories not currently available.

- [x] Standardize a `productionOnly` or equivalent default across broad ranking/discovery tools.
  - Default should bias toward production code unless the user explicitly asks for tests, config, or external concepts.
  - Configuration-first detail: allow per-project defaults in `meridian.json` or analysis options.
  - Indexer update needed: no, if existing file-role and node-type data are sufficient.

- [x] Improve shared confidence labeling so exploratory tools admit uncertainty instead of mixing weak and strong findings.
  - High confidence: exact file-backed production nodes with coherent graph evidence.
  - Medium confidence: structurally valid but partially indirect or weakly protected findings.
  - Low confidence: broad communities, cross-kind rankings, or results polluted by synthetic/external/test noise.
  - Indexer update needed: no.

## Tool-Specific Fixes

### Ranking And Risk Tools

- [x] Reduce noise in `find_hotspots`.
  - Default to production code results first.
  - De-prioritize or suppress tests, config keys, tables, and external concepts unless explicitly requested.
  - Split output by node family when mixed kinds are requested.
  - Configuration-first detail: add project-configurable include/exclude node kinds and file roles.
  - Indexer update needed: optional; current metadata may be enough, but richer node provenance would improve generic suppression.

- [x] Reduce noise in `get_pagerank`.
  - Keep the graph algorithm unchanged, but change presentation/ranking to prioritize actionable production nodes.
  - Suppress mathematically central but operationally low-value artifacts by default.
  - Configuration-first detail: allow a per-project "actionable PageRank" filter profile.
  - Indexer update needed: optional, only for stronger artifact classification.

- [x] Reduce mixed-kind noise in `find_high_churn`.
  - Separate code churn from config churn and external-concept churn.
  - Default summary should rank production code first, with secondary sections for config/external churn.
  - Configuration-first detail: allow projects to opt into dedicated config-health or schema-churn views.
  - Indexer update needed: likely yes if current `changeCount` tracking is not consistently available across node families or if churn should be split by provenance/source category.

- [x] Revisit `find_bridges` ranking presentation.
  - The tool should remain structurally exploratory, but it should emphasize actionable production nodes over mathematically interesting micro-helpers.
  - Add a summary line when no strong production bridge nodes are found.
  - Configuration-first detail: tune the minimum actionability threshold per project.
  - Indexer update needed: no.

### Community And Extraction Tools

- [x] Reduce singleton and micro-community noise in `find_natural_modules`.
  - Default output should collapse or suppress tiny communities and highlight only actionable production clusters.
  - Add summary metrics for omitted singleton/test/config communities.
  - Configuration-first detail: expose minimum community size, minimum production-member ratio, and optional include-test communities.
  - Indexer update needed: no.

- [x] Improve `suggest_extractions` candidate filtering and confidence.
  - Keep the tool generic and safe-first.
  - Require stronger production-member density and better actionability before surfacing a candidate in the main table.
  - Add a secondary section for weak candidates instead of promoting them as primary suggestions.
  - Configuration-first detail: thresholds for community size, production ratio, and confidence cutoffs should be configurable.
  - Indexer update needed: optional; current graph may suffice, but better role/provenance tagging would improve generic filtering.

- [x] Improve `suggest_responsibility_slices` naming quality and slice labeling.
  - Generated slice/service names should be generic, well-formed, and avoid awkward pluralization or malformed labels.
  - Prefer capability- or responsibility-based names over file-local token artifacts.
  - Configuration-first detail: allow naming conventions or preferred namespace roots to be configured per project.
  - Indexer update needed: no.

### Coverage, Routing, And Similarity Tools

- [x] Reduce false-positive noise in `find_coverage_gaps`.
  - Continue surfacing real gaps, but de-prioritize DTOs, tiny value objects, low-risk records, and types that are intentionally exercised indirectly.
  - Separate "high-priority untested behavior" from "low-priority uncalled support types."
  - Configuration-first detail: allow projects to declare ignorable file roles, namespaces, or low-risk type categories.
  - Indexer update needed: optional; richer type-shape metadata would improve generic low-risk suppression.

- [x] Tighten `plan_edit_route` target selection.
  - Route stages should prefer directly actionable production anchors instead of structurally reachable but unrelated nodes.
  - Add stronger validation for route-stage target type and locality.
  - Configuration-first detail: allow per-project route preferences for contracts, infrastructure, CLI, or API surfaces.
  - Indexer update needed: no.

- [x] Reduce test leakage in `find_downstream`.
  - Default downstream traversal for production targets should prioritize production dependencies and move test-only paths into a secondary section.
  - Configuration-first detail: expose whether tests should be included in downstream traversal by default.
  - Indexer update needed: no.

- [x] Improve `find_similar_nodes` default filtering.
  - Default to same node family, same architecture layer when possible, and non-test results first.
  - Keep broader semantic similarity available behind explicit flags.
  - Configuration-first detail: allow layer-sensitive similarity preferences per project.
  - Indexer update needed: optional if layer inference needs richer indexed metadata.

- [x] Revisit `find_duplicate_candidates` grouping and risk presentation.
  - Preserve broad discovery value, but group duplicate families more coherently and separate low-risk extraction candidates from broad incidental similarity.
  - Configuration-first detail: allow project-specific namespace/path exclusions and minimum size thresholds.
  - Indexer update needed: no for grouping; optional for stronger structural metadata.

## Configuration Work

- [x] Add project-level noise-control settings to configuration.
  - Candidates: default `productionOnly`, suppressed node kinds, ignorable file roles, minimum actionable line counts, minimum community size, and route-planning preferences.
  - Keep defaults generic so they work on arbitrary codebases without project tuning.
  - Indexer update needed: no for config shape itself.

- [x] Extend file-role and node-category usage before adding hardcoded special cases.
  - Prefer using existing `meridian.json` file-role patterns and analysis options to describe tests, generated code, config, migrations, and other suppressible surfaces.
  - Indexer update needed: maybe; if current file-role propagation is incomplete, add clearer propagation into indexed node metadata.

## Indexer Follow-Ups

- [x] Audit whether current indexed metadata is sufficient for generic noise suppression.
  - Verify file-role propagation for class/method nodes, not just files.
  - Verify whether nodes can be distinguished as production, test, config, generated, migration, external concept, synthetic helper, or schema artifact without tool-specific heuristics.
  - Audit result: current file-role metadata already flows through C#, TypeScript/TSX, HTML/CSS, configuration ingestion, and Neo4j persistence/retrieval paths, so the application-layer noise reductions can stay generic without a new indexer schema change right now.
  - Indexer update needed: no immediate change required after audit; revisit only if a future tool needs finer provenance than the existing file-role plus node-type signals.

- [x] Add richer provenance/category metadata only if the current graph cannot support low-noise defaults generically.
  - Keep this additive and repo-agnostic.
  - Candidate metadata: source kind, generated/synthetic flag, config artifact flag, external-artifact provenance, and owning file-role snapshot.
  - Audit result: deferred because the current graph shape was sufficient for the low-noise defaults implemented in this plan.
  - Indexer update needed: not currently required.

- [x] Ensure new metadata flows through all language/indexer paths consistently.
  - C#
  - TypeScript/TSX
  - Document indexer
  - Configuration indexer
  - Audit result: existing file-role propagation is already present in the active language/indexer paths used by these tools; no additive metadata rollout was required to finish this plan.
  - Indexer update needed: no.

## Verification

- [x] Add regression tests for each changed tool that prove lower-noise defaults on mixed production/test/config/external datasets.
  - Use compact synthetic graphs in application tests where possible.
  - Add integration tests only where repository behavior or GDS behavior must be exercised.
  - Indexer update needed: only if tests depend on new indexed metadata.

- [x] Add tests for opt-in broader behavior so exploratory use cases are preserved.
  - Example: when `productionOnly=false`, suppressed test/config/external findings should still be reachable.
  - Indexer update needed: no.

- [x] Validate documentation and agent guidance after implementation.
  - Update capability docs and examples for any new flags or default behavior changes.
  - Indexer update needed: no.

## Proposed Implementation Order

- [x] 1. Shared ranking/filtering primitives
- [x] 2. `find_hotspots`, `get_pagerank`, `find_high_churn`
- [x] 3. `find_natural_modules`, `suggest_extractions`
- [x] 4. `find_coverage_gaps`, `find_downstream`, `find_similar_nodes`, `find_duplicate_candidates`
- [x] 5. `plan_edit_route`, `suggest_responsibility_slices`
- [x] 6. Configuration surface
- [x] 7. Indexer metadata follow-ups if still required after the first application-layer pass
- [x] 8. Docs and regression sweep
