# TODO

This is the roadmap index. Each item links to a dedicated note under `docs/features/`.

- [x] [P0 - Add `build_minimal_context`](docs/features/01-add-build-minimal-context.md) - This is the strongest product fit for CodeMeridian
- [x] [P0 - Add Context Detail Levels](docs/features/02-add-context-detail-levels.md) - Token savings only work if every tool avoids returning too much by default
- [x] [P0 - Fix Code Node Embeddings in the Indexers](docs/features/03-fix-code-node-embeddings-in-the-indexers.md) - `find_similar_nodes` already exists and is positioned as duplicate-code discovery, but it does not work unless nodes have embeddings
- [x] [P0 - Fix Stable IDs for Top-Level Local Functions](docs/features/04-fix-stable-ids-for-top-level-local-functions.md) - Top-level local functions in different `Program.cs` files can collide because the generated method ID only uses the signature when no namespace/type exists
- [x] [P1 - Add Token Cost Estimation](docs/features/05-add-token-cost-estimation.md) - The pasted product idea is strongest when CodeMeridian can tell the assistant how much context a task likely needs
- [x] [P1 - Add Complexity-Based Model Guidance](docs/features/06-add-complexity-based-model-guidance.md) - Once token estimates and graph size are known, CodeMeridian can recommend whether a small, fast model is enough or whether a larger context/model is justified.
- [x] [P1 - Improve Test Discovery and Coverage Context](docs/features/07-improve-test-discovery-and-coverage-context.md) - `find_coverage_gaps` is useful, but `build_minimal_context` needs better test relevance
- [x] [P1 - Index Compiler, Analyzer, TypeScript, and Lint Diagnostics](docs/features/08-index-compiler-analyzer-typescript-and-lint-diagnostics.md) - Build errors, compiler warnings, analyzer findings, TypeScript diagnostics, and lint warnings are some of the highest-signal context an AI coding tool can receive
- [x] [P1 - Add Source Snippet Support With Strict Budgets](docs/features/09-add-source-snippet-support-with-strict-budgets.md) - Most context packs should not include source, but sometimes a small method body or interface signature is the most efficient context.
- [x] [P1 - Find Stale Knowledge](docs/features/10-find-stale-knowledge.md) - CodeMeridian persists knowledge across sessions, so it needs a way to detect when remembered docs, manually ingested nodes, external concept links, or agent notes may be stale
- [x] [P1 - Find Implementation Surface](docs/features/11-find-implementation-surface.md) - Graph lookup should help with exact implementation targets, not only broad orientation
- [x] [P1 - Add Graph Freshness And Confidence Signals](docs/features/12-add-graph-freshness-and-confidence-signals.md) - A graph lookup is only as good as the data behind it
- [x] [P1 - Detect Graph Drift Before Implementation](docs/features/13-detect-graph-drift-before-implementation.md) - If CodeMeridian is indexed out of date, the agent can still get the right architectural direction but miss exact file targets
- [x] [P1 - Improve Exact Symbol Resolution](docs/features/14-improve-exact-symbol-resolution.md) - CodeMeridian should be able to move from "this is probably the right file" to "this is the exact method/class ID to edit" more often
- [x] [P1 - Add Index Verification Command](docs/features/15-add-index-verification-command.md) - Exact symbol lookup is much more useful when the user can quickly verify that the local working tree and graph agree before starting implementation.
- [x] [P1 - Add `codemeridian doctor`](docs/features/16-add-codemeridian-doctor.md) - First-run setup has several moving parts: Docker, Neo4j, MCP server, auth, `.env`, `meridian.json`, embeddings, and indexed data
- [x] [P1 - Add Session Usefulness Evaluation](docs/features/17-add-session-usefulness-evaluation.md) - CodeMeridian should be able to answer whether it actually helped an implementation session
- [x] [P1 - Package the Indexers for Easier Use](docs/features/18-package-the-indexers-for-easier-use.md) - The old language-specific indexer commands worked for contributors, but they were not a polished user experience
- [x] [P2 - Improve Cross-Language Connection Quality](docs/features/19-improve-cross-language-connection-quality.md) - Current C# and TypeScript indexing share the graph, but true frontend-to-backend tracing needs stronger edges than imports and class relationships.
- [ ] [P2 - Add HTML / CSS / SCSS Relationship Indexing](docs/features/20-add-html-css-scss-relationship-indexing.md) - Frontend context is not only TypeScript
- [ ] [P2 - Add CI-Friendly Context Reports](docs/features/21-add-ci-friendly-context-reports.md) - CodeMeridian can produce PR context summaries without relying on an interactive assistant.
- [x] [P2 - Add Duplicate-Code Workflow](docs/features/22-add-duplicate-code-workflow.md) - Once embeddings work, CodeMeridian can turn `find_similar_nodes` into a practical duplicate-code review flow.
- [ ] [P3 - Add More External Concept Indexers](docs/features/23-add-more-external-concept-indexers.md) - `link_external_concept` is powerful, but manual linking limits adoption.

- [x] [P2 - Add Change-Route Planning](docs/features/24-add-change-route-planning.md) - Give the AI an ordered edit path instead of a file dump.
- [x] [P2 - Add Bridge Node Detection](docs/features/25-add-bridge-node-detection.md) - Find small but structurally important nodes that connect separate parts of the system.
- [x] [P2 - Add Natural Module Detection](docs/features/26-add-natural-module-detection.md) - Discover modules from the graph instead of from folders.
- [x] [P2 - Add Architecture Erosion Timeline](docs/features/27-add-architecture-erosion-timeline.md) - Track how architecture gets worse over time.
- [x] [P2 - Add Test Shield Map](docs/features/28-add-test-shield-map.md) - Show which tests protect a change path.
- [x] [P2 - Add Refactor Extraction Candidates](docs/features/29-add-refactor-extraction-candidates.md) - Find tightly connected groups that are good extraction targets.
- [x] [P2 - Add Blast Radius With Confidence](docs/features/30-add-blast-radius-with-confidence.md) - Make impact analysis explicit about what is proven versus inferred.
- [x] [P2 - Add Path-Explained Context Packs](docs/features/31-add-path-explained-context-packs.md) - Explain why each file is included in a context pack.
- [x] [P2 - Add Semantic Graph Hybrid Search](docs/features/32-add-semantic-graph-hybrid-search.md) - Mix embeddings with graph constraints for better retrieval.
- [x] [P2 - Add Dependency Smell Paths](docs/features/33-add-dependency-smell-paths.md) - Surface architecture rule violations as graph paths.
- [x] [P2 - Add Safe Replacement Surface Guidance](docs/features/34-add-safe-replacement-surface-guidance.md) - Group replacement work into safe and risky clusters.
- [x] [P2 - Add Knowledge Decay Graph](docs/features/35-add-knowledge-decay-graph.md) - Turn stale-knowledge detection into a graph-native view.
- [ ] [P2 - Add Feature To Code Map](docs/features/36-add-feature-to-code-map.md) - Make features first-class graph nodes linked to code.
- [ ] [P2 - Add Endpoint To Database Tracing](docs/features/37-add-endpoint-to-database-tracing.md) - Trace a web request through the full vertical slice.
- [ ] [P3 - Add Architecture Weather Report](docs/features/38-add-architecture-weather-report.md) - Summarize graph health in a quick status report.
- [ ] [P1 - Add Precision Feedback Loop](docs/features/39-add-precision-feedback-loop.md) - Use session evaluation results to improve future target ranking.
- [ ] [P1 - Add Implementation Surface Pruning](docs/features/40-add-implementation-surface-pruning.md) - Return fewer, better files by collapsing broad graph matches into edit-ready targets.
- [ ] [P2 - Add Test Target Precision](docs/features/41-add-test-target-precision.md) - Suggest the tests most likely to change or run, not every loosely related shield.
- [x] [P2 - Add Responsibility Slice Suggestions](docs/features/42-add-responsibility-slice-suggestions.md) - Suggest folder, namespace, and service extraction slices for large classes by clustering methods through graph evidence such as shared dependencies, callers, MCP tools, endpoints, CLI commands, DTOs, tests, docs, and architecture boundaries.
- [x] [P2 - Add Context Workflow Planning](docs/features/43-add-context-workflow-planning.md) - Let agents ask CodeMeridian to plan the correct sequence of existing CodeMeridian tools for a task, using actual tool recipes for before-edit checks, feature implementation, refactor planning, responsibility slicing, architecture review, dependency replacement, diagnostics, configuration review, semantic discovery, knowledge health, ingestion, and extension-agent routing