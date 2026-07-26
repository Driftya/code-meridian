# CodeMeridian Tool Surface Evaluation

- Date: 2026-07-25
- Project: `CodeMeridian`
- Server/client build: `1.0.0+8f2be555e0159fa16da23a5ab6c4ac30837c7a0a`
- Graph contract: `4`
- Cache contract: `3`
- Scope: all 62 MCP tools, supporting CLI health behavior, automated tests, and dependency audits
- Method: live read-only dogfood calls, guarded destructive calls, automated contract/integration tests for mutating tools, and source verification for suspicious results

## Executive Summary

CodeMeridian is operational and broadly useful, but the complete surface is not yet uniformly correct.

The server, MCP endpoint, Neo4j connection, GDS procedures, keyword graph, diagnostics, configuration graph, frontend graph, and client-extension contracts all responded successfully. No MCP call crashed or returned a transport-level failure.

The evaluation classified the 62 tools as follows:

| Result | Count | Meaning |
|---|---:|---|
| Live pass | 43 | Returned a coherent result or a correct bounded empty result |
| Live degraded | 9 | Call completed, but the result or handoff has a reproducible correctness/usability defect |
| Optional capability unavailable | 2 | Correctly explained that embeddings are disabled |
| Live maintenance/guard pass | 2 | Document ingestion and destructive confirmation guard |
| Contract/integration tested only | 6 | Mutating/destructive operation was not run against the live project |

## Implementation Follow-Up

The defects identified by this evaluation have been corrected in the subsequent local implementation:

- canonical structural-query retries now resolve the supplied node ID directly;
- required workflow steps stop and fail on semantic resolution failures;
- edit-route anchors use symbol identity evidence instead of summary/source noise;
- recent-change results exclude operational diagnostic nodes;
- leaf configuration usage includes clearly labeled, confidence-capped parent-section bindings;
- stale-knowledge heuristics ignore fenced examples and explicit prohibition lines;
- node-scoped empty diagnostics no longer imply that global diagnostic indexing is disabled;
- responsibility-slice guidance recommends reindexing only when indexed member evidence is absent.

Embedding health was also made observable: doctor now separates configured state, provider reachability, provider/dimensions, and stored embedded-node count. Project-level embedding expectation is supported by `meridian.json`, while provider configuration remains server-side.

Local acceptance after implementation: 930 .NET tests passed, 75 JavaScript tests passed, npm reported 0 vulnerabilities, and NuGet reported no vulnerable packages. A new live surface evaluation is still required after deploying this implementation and reindexing.

The most important defect is a broken canonical-symbol handoff: `query_codebase` tells callers to retry an ambiguous structural query with a canonical ID, but the canonical retry fails. `execute_context_workflow` reproduces the same mismatch internally, marks the failed required resolution step as completed, emits no warning, and reports the overall workflow as completed.

Graph metadata itself is fresh. Relationship completeness is correctly reported as Low, so graph-dependent tools appropriately include trust warnings. That is a known data-quality condition, not an MCP deployment failure.

## Baseline Health

### Live service

- Client and server versions match exactly.
- MCP endpoint reachable: yes.
- Neo4j reachable: yes.
- Indexed nodes: 6,525.
- Call edges: 3,940.
- Ordinary diagnostics: 77.
- Error-level diagnostics: 0.
- Graph drift: Moderate because unresolved-local and indeterminate relationships remain.
- Sampled metadata freshness: 20 High, 0 Medium, 0 Low.
- Missing file paths, lines, hashes, and timestamps: 0.
- Stored-role/test-path conflicts: 0.

### Automated verification

| Check | Result |
|---|---|
| `dotnet test CodeMeridian.sln --no-restore` | 924 passed, 0 failed |
| `npm test` | 75 passed, 0 failed |
| `npm audit --audit-level=high` | 0 vulnerabilities |
| `dotnet list CodeMeridian.sln package --vulnerable --include-transitive` | no vulnerable packages |

The .NET build emits existing nullable warnings in test projects. Those warnings match the 77 indexed ordinary diagnostics.

## Prioritized Findings

### CM-EVAL-001 — Canonical structural-query retry is broken

- Priority: P1
- Tools: `query_codebase`
- Classification: correctness/contract defect

Reproduction:

1. `query_codebase("callers of DeleteDiagnosticsAsync")` correctly reports two ambiguous targets.
2. It instructs the caller to choose a canonical ID and retry.
3. Retrying with the returned canonical implementation ID reports:
   `No exact structural target found ... Use resolve_exact_symbol or provide a canonical node ID.`

The caller already provided the exact canonical node ID returned by CodeMeridian. This makes the advertised ambiguity-resolution flow impossible through `query_codebase`.

Recommended fix:

- Recognize canonical IDs before parsing the structural target as a name.
- Add a round-trip test: ambiguous name -> returned canonical ID -> callers result.

### CM-EVAL-002 — Workflow execution hides a failed required resolution step

- Priority: P1
- Tools: `execute_context_workflow`
- Related tool: `plan_context_workflow`
- Classification: workflow correctness defect

A `before_edit` workflow with a valid canonical target produced:

- step 1 `resolve_exact_symbol`: status `completed`
- output: `No exact symbol candidates found`
- workflow status: `completed`
- warnings: none

Later tools accepted the same canonical target and returned useful context, so the target was valid. The workflow conflates “tool call returned text” with “required step succeeded.”

Recommended fix:

- Skip name resolution when the target is already a canonical ID, or resolve canonical IDs directly.
- Parse required-step outcome contracts.
- Mark the step failed/degraded and stop or warn when exact resolution returns no candidates.

### CM-EVAL-003 — Edit-route anchor can be unrelated to the requested behavior

- Priority: P2
- Tool: `plan_edit_route`
- Classification: ranking/precision defect

For “change diagnostic replacement while preserving compatibility metadata,” the route selected `FindImpactPathsAsync` as its anchor. The resulting route still included useful diagnostics contracts and repository targets, but its primary anchor was unrelated to diagnostic replacement.

Recommended fix:

- Require the anchor to match at least one high-value goal concept in its symbol, declaring type, file, or directly linked feature documentation.
- Add an acceptance fixture for diagnostic replacement and IndexRun preservation.

### CM-EVAL-004 — Recently changed code includes diagnostics

- Priority: P2
- Tool: `find_recently_changed`
- Classification: result-contract defect

The tool description promises “code nodes,” but the live result ranks newly ingested `Diagnostic` nodes alongside code changes.

Recommended fix:

- Restrict the default result to code-node labels.
- If diagnostics are useful, expose them in a separate section or behind an explicit option.

### CM-EVAL-005 — Leaf configuration usage misses section binding

- Priority: P2
- Tool: `find_config_usage`
- Classification: configuration precision gap

Evidence:

- `find_config_definitions("Neo4j:Uri")` finds three definitions.
- `find_config_usage("Neo4j:Uri")` reports no usage.
- `find_config_usage("Neo4j")` finds both `ReadsConfig` and `BindsConfig` edges in `AddInfrastructure`.
- Source binds the `Neo4j` section to `Neo4jOptions`, which contains the `Uri` property.

The tool documentation uses `Neo4j:Uri` as its example, so returning no usage for the leaf key is misleading when a typed section binding consumes it.

Recommended fix:

- Expand leaf usage through a bound options section and matching property.
- Distinguish direct leaf reads from inferred typed-section consumption.

### CM-EVAL-006 — Stale-knowledge analysis treats examples and prohibitions as references

- Priority: P2
- Tools: `find_stale_knowledge`, `knowledge_decay`
- Classification: false-positive/noise defect

Examples include documentation that says not to use `Thread.Sleep`, and test-project names used as examples or command guidance. These are reported as unresolved code references even though they are prose, prohibitions, or examples.

Recommended fix:

- Down-rank code-shaped tokens inside fenced examples, command blocks, and explicit “avoid/do not use” prose.
- Separate possible references from strong stale-reference evidence.

### CM-EVAL-007 — Empty nearby-diagnostic guidance is misleading

- Priority: P3
- Tool: `find_diagnostics_for_node`
- Classification: empty-state wording defect

For a production node with no nearby diagnostics, the tool says:

`No indexed diagnostics found. Run the indexer with --include-diagnostics.`

The project has 77 indexed diagnostics. A positive test against `GraphQueryServiceTests` correctly returned four nearby diagnostics, proving the feature is active.

Recommended fix:

- Say “No diagnostics found in this node’s file.”
- Recommend indexing diagnostics only when the project has no ordinary diagnostic records.

### CM-EVAL-008 — Responsibility-slice guidance recommends an unnecessary reindex

- Priority: P3
- Tool: `suggest_responsibility_slices`
- Classification: guidance defect

The tool found 23 methods for `CodebaseQueryService` on a fresh full graph, then recommended reindexing “if the target is known to have method members.”

Recommended fix:

- Emit reindex guidance only when no member evidence exists or freshness is stale.

## Optional Capabilities

The following tools behaved correctly but could not produce semantic results because embeddings are disabled:

- `find_similar_nodes`
- `hybrid_search`

Both returned clear enablement/reindex guidance. This is a deployment capability choice, not a defect.

## Complete Tool Matrix

### Discovery, exact targeting, and context

| Tool | Result | Notes |
|---|---|---|
| `query_codebase` | Degraded | Open search and ambiguity detection work; canonical retry is broken |
| `get_architectural_overview` | Pass | Returned bounded namespace/class/interface overview |
| `resolve_exact_symbol` | Pass | Exact class and file/line-assisted resolution worked |
| `get_context_for_editing` | Pass | Returned callers, callees, files, and relationship warning |
| `build_minimal_context` | Pass | Returned bounded context, tests, paths, and confidence |
| `find_impact` | Pass | Backward impact traversal returned ranked evidence |
| `find_downstream` | Pass | Forward dependency traversal returned bounded evidence |
| `find_connection` | Pass | Returned a direct one-hop test relationship |

### Planning and workflow

| Tool | Result | Notes |
|---|---|---|
| `find_implementation_surface` | Pass | Exact diagnostic repository target ranked first |
| `analyze_feature_implementation_path` | Pass | Found plan, code, tests, status, and risk |
| `find_implementation_patterns` | Pass | Returned structurally ranked patterns |
| `plan_edit_route` | Degraded | Useful route, unrelated primary anchor |
| `replace_surface` | Pass | Correct bounded empty result for unused dependency |
| `plan_context_workflow` | Pass | Deterministic valid workflow plan |
| `execute_context_workflow` | Degraded | Required resolution failure hidden as completed |
| `suggest_responsibility_slices` | Degraded | Useful defer recommendation, inaccurate reindex guidance |

### Tests, risk, and architecture

| Tool | Result | Notes |
|---|---|---|
| `find_test_shield` | Pass | Direct and indirect test evidence returned |
| `find_coverage_gaps` | Pass | Bounded production candidates with relationship warning |
| `find_hotspots` | Pass | Production-only ranking; test paths excluded |
| `find_unreferenced` | Pass | Entry-point/DI caveat included |
| `find_cross_project_dependencies` | Pass | Correct bounded empty result for one project context |
| `find_recently_changed` | Degraded | Diagnostic nodes mixed into code-node result |
| `find_large_nodes` | Pass | Deterministic thresholded result |
| `find_god_classes` | Pass | Size and caller evidence rendered |
| `find_cycles` | Pass | Canonical namespace-pair results |
| `find_architecture_violations` | Pass | No configured forbidden edges found |
| `find_smell_paths` | Pass | No forbidden paths found |
| `architecture_drift_history` | Pass | Clearly labels projected current-state history limits |
| `find_high_churn` | Pass | Production/broader/noise partition rendered |
| `analyze_changed_subgraph` | Pass | C# and TypeScript changed-file projection worked |
| `find_graph_drift` | Pass | Moderate relationship drift and clean structural signals |
| `check_graph_freshness` | Pass | High node freshness kept separate from Low relationships |

### GDS and semantic analysis

| Tool | Result | Notes |
|---|---|---|
| `get_pagerank` | Pass | GDS result returned and production candidates prioritized |
| `get_betweenness` | Pass | GDS bridge ranking returned |
| `find_bridges` | Pass | Combined structural evidence and next-tool guidance |
| `find_natural_modules` | Pass | GDS completed; current graph produced mostly micro-communities |
| `suggest_extractions` | Pass | Correctly returned no primary candidate above thresholds |
| `find_similar_nodes` | Optional unavailable | Embeddings disabled; clear guidance returned |
| `hybrid_search` | Optional unavailable | Embeddings disabled; clear guidance returned |
| `find_duplicate_candidates` | Pass | Frontend exact-shape clusters returned |

### Documentation, keyword, and dependency knowledge

| Tool | Result | Notes |
|---|---|---|
| `search_documentation` | Pass | Found the exact relationship-health plan and related docs |
| `find_related_knowledge` | Pass | Returned lexical code/doc/test relationships |
| `find_stale_knowledge` | Degraded | Excess false positives from examples/prohibitions |
| `knowledge_decay` | Degraded | Alias correctly matches, including the same noise |
| `find_tool_dependency_impact` | Pass | Hard and awareness consumers returned |
| `rebuild_keyword_graph` | Contract/integration tested | Live project mutation not repeated |
| `classify_keywords` | Contract/integration tested | Live project mutation not repeated |

### Diagnostics, configuration, frontend, and endpoint tracing

| Tool | Result | Notes |
|---|---|---|
| `find_diagnostics` | Pass | 77 ordinary diagnostics; IndexRun records excluded |
| `find_diagnostics_for_node` | Degraded | Positive lookup works; empty-state guidance is wrong |
| `find_config_definitions` | Pass | JSON and environment override forms normalized |
| `find_config_usage` | Degraded | Root section works; typed leaf consumption is missed |
| `find_frontend_cascade_conflicts` | Pass | 13 inferred conflicts and 2 specificity warnings |
| `trace_endpoint` | Pass | Correct bounded no-trace result with tracing guidance |

### Client extension tools

| Tool | Result | Notes |
|---|---|---|
| `get_client_extension_contract` | Pass | Versioned GraphQL/auth/limit contract returned |
| `list_client_extension_examples` | Pass | Curated deterministic examples listed |
| `get_client_extension_example` | Pass | GraphQL document, variables, and result shape returned |

### Graph mutation and maintenance

| Tool | Result | Notes |
|---|---|---|
| `ingest_document` | Live maintenance pass | This evaluation report was ingested and searched |
| `clear_code_graph` | Guard pass | `confirm=false` correctly refused deletion; success path is tested |
| `ingest_code_node` | Contract tested | Validation, embedding parsing, and upsert wrapper tests pass |
| `ingest_relationship` | Contract tested | Relationship-type validation and SDK tests pass |
| `link_external_concept` | Contract tested | Directional node/edge wrapper test passes |
| `clear_project_knowledge` | Contract tested | Wrapper and SDK request tests pass |

## Tooling Coverage Observations

- The authoritative server registration exposes 62 MCP tools.
- Only 35 tool names appear directly in test source text. This is not equivalent to only 35 tested tools: many are covered through service-method tests, shared wrappers, and integration repositories.
- Direct per-tool contract tests would make omissions easier to detect, especially for planner/executor handoffs and empty-state wording.
- The current suites are strong on service behavior and persistence, but live composition tests are still necessary to catch the canonical-ID and result-quality defects above.

## Recommended Remediation Order

1. Fix canonical-ID handling in `query_codebase` and `execute_context_workflow`.
2. Make workflow required-step status semantic rather than transport-only.
3. Add exact anchor acceptance tests for `plan_edit_route`.
4. Restrict `find_recently_changed` to code nodes by default.
5. Expand configuration leaf usage through typed section bindings.
6. Reduce stale-knowledge false positives from examples and prohibitions.
7. Correct the two misleading guidance messages.
8. Re-run this same 62-tool matrix after deployment.

## Overall Assessment

CodeMeridian’s core graph access, impact analysis, test provenance, GDS integration, documentation lookup, diagnostics counting, frontend analysis, and client extension surfaces are functioning. The system is suitable for continued dogfooding with relationship-confidence warnings respected.

It should not yet be described as “all tools work exactly as intended.” The two canonical-ID/workflow defects can break a normal agent handoff, and the other degraded tools can produce avoidable false positives or misleading guidance. None of the findings indicate data loss, server instability, or a failed deployment.
