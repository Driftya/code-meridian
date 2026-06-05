# TODO

This roadmap prioritizes the next best investments for CodeMeridian based on the current feature set and the product direction of making AI coding context smaller, more precise, and more reliable.

## Best Next Investment

### - [x] P0 - Add `build_minimal_context`

**Why:** This is the strongest product fit for CodeMeridian. Existing tools already expose the raw ingredients: `get_context_for_editing`, `find_impact`, `find_connection`, `find_coverage_gaps`, `search_documentation`, and `link_external_concept`. A dedicated context-pack builder turns those into the primary value proposition: give Copilot the smallest useful context slice instead of dumping files.

**Outcome:** A coding assistant can ask for a bounded context pack before editing and get only the target, callers, callees, tests, external concepts, and risk notes that matter.

**Suggested tool shape:**

```json
{
  "target": "Method:Payments.OrderService.PlaceOrderAsync(Order,CancellationToken)",
  "goal": "add idempotency key support",
  "maxTokens": 3000,
  "includeTests": true,
  "includeExternalConcepts": true,
  "includeSourceSnippets": false,
  "detailLevel": "Compact"
}
```

**Default output should include:**

- Target node metadata: name, type, file, line, size, summary
- Direct callers and callees
- Implemented interfaces and implementing classes
- Relevant tests or coverage gaps
- Linked external concepts: tables, APIs, topics, services
- Recently changed or high-churn risk signals
- Files likely needed for the edit
- Estimated context token cost

**Effort:** Medium  
**Value:** Very high  
**Risk:** Low, because it can compose existing repository/service methods first.

## - [x] P0 - Add Context Detail Levels

**Why:** Token savings only work if every tool avoids returning too much by default. The default should be compact, with expansion available when explicitly requested.

**Suggested enum:**

```csharp
public enum ContextDetailLevel
{
    Summary,
    Compact,
    Full
}
```

**Apply to:**

- `get_context_for_editing`
- `find_impact`
- `find_downstream`
- `find_connection`
- `find_coverage_gaps`
- `find_large_nodes`
- New `build_minimal_context`

**Rules:**

- `Summary`: names, paths, relationship types, risk score
- `Compact`: summary plus top relevant metadata
- `Full`: only when the caller explicitly asks for source snippets or expanded detail

**Effort:** Medium  
**Value:** High  
**Risk:** Medium, because output tests will need updates.

## - [x] P0 - Fix Code Node Embeddings in the Indexers

**Why:** `find_similar_nodes` already exists and is positioned as duplicate-code discovery, but it does not work unless nodes have embeddings. The current indexers ingest nodes without embeddings, so a high-value feature appears broken during real use.

**Tasks:**

- Add optional embedding generation to C# indexing.
- Add optional embedding generation to TypeScript indexing.
- Make embedding generation opt-in by env/config so local indexing remains cheap.
- Document required model, dimensions, and cost behavior.
- Add a clear indexer warning when `find_similar_nodes` cannot work because embeddings are absent.

**Effort:** Medium to high  
**Value:** High  
**Risk:** Medium, mostly around model/provider configuration and indexing speed.

## - [x] P0 - Fix Stable IDs for Top-Level Local Functions

**Why:** Top-level local functions in different `Program.cs` files can collide because the generated method ID only uses the signature when no namespace/type exists. This caused duplicate `IsAuthorized(HttpRequest,string)` functions to collapse into one graph node.

**Tasks:**

- Include file identity or container identity in IDs for local functions.
- Preserve stable IDs for normal class methods where possible.
- Add tests for two top-level `Program.cs` files with same local function names.
- Consider a migration note: clear and re-index projects after this change.

**Effort:** Low to medium  
**Value:** High  
**Risk:** Medium, because node ID changes can affect existing graph references.

## - [x] P1 - Add Token Cost Estimation

**Why:** The pasted product idea is strongest when CodeMeridian can tell the assistant how much context a task likely needs. This enables better model selection and prevents unnecessary file loading.

**Suggested estimate model:**

- Node metadata row: 20 tokens
- Relationship row: 15 tokens
- Method summary: 80 tokens
- Class summary: 150 tokens
- Source snippet: character count / 4
- Documentation excerpt: character count / 4

**Output example:**

```text
Estimated context: 2,400 tokens
Small model likely sufficient.
Expansion risk: low, 4 direct callers, 3 direct callees, 1 related test.
```

**Effort:** Low  
**Value:** High  
**Risk:** Low, estimates do not need to be exact to be useful.

**Implemented:** `build_minimal_context` now reports an approximate token estimate using target metadata, graph rows, summaries, likely files, optional source snippets, and test context. The output states whether the pack fits the requested `maxTokens` budget and gives expansion-risk guidance when it does not.

## - [x] P1 - Add Complexity-Based Model Guidance

**Why:** Once token estimates and graph size are known, CodeMeridian can recommend whether a small, fast model is enough or whether a larger context/model is justified.

**Signals:**

- Estimated token count
- Number of affected nodes from `find_impact`
- Number of downstream dependencies
- Cross-project edges
- High-churn or hotspot status
- Missing tests
- External concepts involved

**Output example:**

```text
Model guidance: use a larger model.
Reason: estimated 18,000 tokens, 42 affected nodes, 3 cross-project dependencies, missing test coverage.
```

**Effort:** Low to medium  
**Value:** Medium to high  
**Risk:** Low.

**Implemented:** Context packs now include a `Complexity`, `Model guidance`, and `Expansion risk` line. Guidance is based on estimated tokens, affected nodes, downstream dependencies, cross-project graph edges, nearby coverage gaps, related-test availability, target size, and indexed churn.

## - [x] P1 - Improve Test Discovery and Coverage Context

**Why:** `find_coverage_gaps` is useful, but `build_minimal_context` needs better test relevance. A context pack should identify tests that call the target, nearby tests by namespace/file, and missing tests.

**Tasks:**

- Add a repository query for tests related to a node.
- Include direct test callers when available.
- Fall back to namespace/file-name similarity.
- Mark heuristic matches clearly.

**Effort:** Medium  
**Value:** High  
**Risk:** Medium, because test relationships can be incomplete without better call resolution.

**Implemented:** Added `FindRelatedTestsAsync` to the graph repository and included it in `build_minimal_context`. Context packs now show direct test callers separately from heuristic test matches, include test files in the likely-file list, and keep nearby coverage gaps for missing-test context. Heuristic matches use test namespace/file detection plus namespace, file-name, and node-name similarity, and are labeled explicitly. Neo4j now stores indexed normalized fields (`nameNormalized`, `namespaceNormalized`, `filePathNormalized`, `projectContextNormalized`) so case-insensitive test and diagnostic queries avoid per-row `toLower(...)` work.

## - [x] P1 - Index Compiler, Analyzer, TypeScript, and Lint Diagnostics

**Why:** Build errors, compiler warnings, analyzer findings, TypeScript diagnostics, and lint warnings are some of the highest-signal context an AI coding tool can receive. They tell the assistant what is already broken, what code style rules matter in the project, and which files need attention before a change is safe.

**Outcome:** CodeMeridian can answer questions like:

- What warnings exist near this method?
- Which changed files currently fail lint or type checking?
- What diagnostics should Copilot fix first?
- Does this refactor introduce new compiler or ESLint warnings?

**C# sources to support:**

- `dotnet build` diagnostics from MSBuild output.
- Roslyn compiler warnings and errors.
- Analyzer diagnostics from the project's existing `.editorconfig`, `Directory.Build.props`, package analyzers, and rulesets.
- Optional `dotnet format --verify-no-changes` style diagnostics later.

**TypeScript / JavaScript sources to support:**

- `tsc --noEmit` diagnostics using the project's own `tsconfig.json`.
- ESLint diagnostics using the project's own config when present.
- Prefer package scripts first, such as `npm run lint`, `pnpm lint`, or `yarn lint`, because projects often wrap ESLint with the correct flags.
- Fall back to local binaries: `node_modules/.bin/eslint` and `node_modules/.bin/tsc`.

**Graph model:**

- Add a `Diagnostic` node type or equivalent external concept type.
- Store severity, code/rule ID, message, file, line, column, source tool, and project context.
- Link diagnostics to the nearest file node and, when possible, nearest class/method node by line range.
- Track timestamps so fixed diagnostics disappear after re-indexing or can be shown as recently resolved.

**Suggested tools:**

- `find_diagnostics`
- `find_diagnostics_for_node`
- `find_diagnostics_for_project`
- Include diagnostics in `build_minimal_context` by default in compact form.

**Important config behavior:**

- Do not invent lint rules. Use the target project's existing config.
- Detect package manager lockfiles and scripts before choosing commands.
- Allow diagnostics indexing to be disabled or run separately because lint/build commands can be slow or require dependencies.
- Mark unavailable diagnostics clearly when dependencies are missing or scripts fail.

**Effort:** Medium to high  
**Value:** High  
**Risk:** Medium, because command execution differs across repositories and ESLint config discovery can be messy.

**Implemented first slice:** Diagnostics are indexed as `Diagnostic` code nodes by default, using `dotnet build --no-restore --nologo`, local `tsc --noEmit --pretty false`, and project lint scripts or local ESLint when available. Use `--skip-diagnostics` for faster structural-only indexing. Query tools `find_diagnostics` and `find_diagnostics_for_node` expose the results.

## - [ ] P1 - Add Source Snippet Support With Strict Budgets

**Why:** Most context packs should not include source, but sometimes a small method body or interface signature is the most efficient context.

**Rules:**

- Disabled by default.
- Include snippets only for target node and top-ranked direct dependencies.
- Respect `maxTokens`.
- Truncate with clear markers.
- Never return whole files unless explicitly requested.

**Effort:** Medium  
**Value:** Medium  
**Risk:** Medium, because source extraction must stay predictable.

## - [x] P1 - Find Stale Knowledge

**Why:** CodeMeridian persists knowledge across sessions, so it needs a way to detect when remembered docs, manually ingested nodes, external concept links, or agent notes may be stale. Without this, persistent memory can quietly turn into misleading memory.

**Suggested tool:** `find_stale_knowledge`

**Example prompt:**

```text
@copilot What CodeMeridian knowledge might be stale?
```

**Signals to include:**

- Linked external concepts point to missing or deleted code nodes
- Documentation mentions method or class names that no longer exist
- Manual relationships target renamed nodes
- Agent memory is older than the last major reindex
- High number of orphaned nodes

**Output example:**

```text
Possibly stale knowledge:

- ADR-004 references PaymentGateway.ChargeAsync, but the node was renamed.
- External concept "orders table" is linked to old OrderRepository.SaveAsync.
- 12 manual relationships target nodes not updated in 30 days.
```

**Relationship guidance:**

- Prefer a weak `Mentions` or `References` edge from knowledge documents, agent notes, and external concepts to code nodes.
- Keep the relationship directional from knowledge to code so the fact source stays explicit.
- Query it in reverse when needed; the graph can still traverse incoming edges from a code node to find related knowledge.
- Do not model vague semantic similarity as a hard dependency.
- If a knowledge item points at a deleted or renamed node, mark it as stale rather than silently rewiring it.

**Implementation notes:**

- Include the last reindex time and the target node's current existence in the check.
- Surface orphaned docs and notes separately from likely-renamed references.
- Treat stale external concepts as a soft warning, not an automatic deletion.
- Keep the result explainable so the assistant can tell the user why something looks stale.

**Effort:** Medium  
**Value:** High  
**Risk:** Medium, because the stale check needs enough metadata to avoid false positives.

**Implemented:** Added `find_stale_knowledge` to surface unresolved doc references, orphaned external concepts, old notes, and orphaned code nodes. Documents can now carry weak `relatedNodeIds` metadata, which is stored as `Mentions` edges from `KnowledgeDocument` nodes to current `CodeNode` targets when available.

## - [x] P1 - Find Implementation Surface

**Why:** Graph lookup should help with exact implementation targets, not only broad orientation. When CodeMeridian can only point at a layer or repository surface, the agent still has to fall back to manual file inspection and loses most of the time saved by the graph.

**Goal:** Given a feature goal or concept cluster, return the most likely files, classes, and methods to edit.

**Suggested tool:** `find_implementation_surface`

**Example prompt:**

```text
@copilot What is the best implementation surface for adding stale-knowledge detection?
```

**Desired output:**

- Likely implementation files
- Likely methods to extend
- Why each target was chosen
- Confidence level per target
- Whether the graph data is fresh enough to trust

**Useful signals:**

- Related tool names and service names
- API endpoint names
- Repository methods with matching concepts
- Tests that already cover the same behavior
- Recent churn in the same area
- Exact node IDs when available

**Effort:** Medium  
**Value:** High  
**Risk:** Medium, because the heuristic needs to stay explainable and avoid noisy target suggestions.

**Implemented:** Added `find_implementation_surface`, which ranks likely implementation files from graph matches, concept matches, likely methods/classes, and local freshness checks. Results include confidence, target files, likely symbols, reasons, and freshness status so agents can report whether CodeMeridian provided exact targets or only general areas.

## - [x] P1 - Add Graph Freshness And Confidence Signals

**Why:** A graph lookup is only as good as the data behind it. The assistant needs to know whether a result is exact, heuristic, stale, or partially verified before it decides whether to trust the graph or fall back to source files.

**Add to result payloads:**

- `indexedAt`
- `updatedAt`
- `fileExists`
- `lineRangeStillValid`
- `nodeIdConfidence`
- `freshnessReason`

**Desired behavior:**

- Exact node hits should say why they are exact.
- Heuristic matches should say what was inferred.
- Stale or missing source should be explicit.
- Tools should return a short trust summary, not just raw facts.

**Effort:** Medium  
**Value:** High  
**Risk:** Medium, because it touches many result formatters and tool descriptions.

**Implemented first slice:** Added `check_graph_freshness`, which reports `updatedAt`, file existence, line-range validity, confidence, and reason for matching graph nodes. `find_implementation_surface` also includes per-target freshness and confidence. Broader annotation across every existing formatter can be expanded later if needed.

## - [x] P1 - Detect Graph Drift Before Implementation

**Why:** If CodeMeridian is indexed out of date, the agent can still get the right architectural direction but miss exact file targets. A built-in drift check would tell the agent whether it should trust the graph or re-index first.

**Suggested tool:** `find_graph_drift`

**Possible checks:**

- Nodes whose files no longer exist
- Nodes whose line ranges no longer fit the source file
- Nodes updated before the last major reindex
- Projects with many renamed or deleted files
- Query surfaces that return broad layers but no exact method IDs

**Desired output example:**

```text
Graph drift: moderate
Reason: 14 nodes point to missing files, 6 node line ranges no longer match source, and the last reindex predates the latest rename.
Recommendation: run `codemeridian index . --project CodeMeridian --clear`
```

**Effort:** Medium  
**Value:** High  
**Risk:** Medium, because drift detection needs accurate file and timestamp metadata.

**Implemented:** Added `find_graph_drift`, which checks indexed nodes for missing files, invalid line ranges, and missing timestamps, then reports drift severity and a re-index recommendation when exact implementation targeting should not be trusted.

## - [x] P1 - Improve Exact Symbol Resolution

**Why:** CodeMeridian should be able to move from "this is probably the right file" to "this is the exact method/class ID to edit" more often. Today, implementation-surface lookup can still find broad layers when exact method IDs are missing, stale, duplicated, or not indexed with enough source precision.

**Goal:** Make graph lookup behave more like an implementation navigator, not only an architectural map.

**Tasks:**

- Add an exact symbol lookup path that accepts method/class names, file paths, and line hints.
- Return canonical node IDs alongside every implementation-surface result when available.
- Detect when a file has graph nodes but no method/class near the requested line or concept.
- Report "exact", "file-only", "heuristic", or "stale" target confidence.
- Improve indexer coverage for nested classes, partial classes, local functions, top-level functions, overloads, generated-file exclusions, and language-specific edge cases.
- Add a `codemeridian index --verify` or equivalent drift check that compares graph nodes against the current working tree before implementation work.
- Add integration tests that prove exact method IDs can be found after indexing this repository.

**Effort:** Medium to high  
**Value:** Very high  
**Risk:** Medium, because stable IDs and language-specific symbol models can affect existing graph references.

**Implemented:** Added `resolve_exact_symbol`, which resolves symbol, file, and line hints to canonical node IDs with `exact`, `file-only`, `heuristic`, or `stale` confidence. `find_implementation_surface` now includes canonical IDs and target confidence in its result table. `CodeGraphQuery` supports file-path filtering so exact symbol lookup can query Neo4j directly by indexed file path instead of filtering after a broad result limit. The remaining CLI-level `codemeridian index --verify` idea is tracked separately below.

## - [ ] P1 - Add Index Verification Command

**Why:** Exact symbol lookup is much more useful when the user can quickly verify that the local working tree and graph agree before starting implementation.

**Goal:** Add a CLI command or flag that runs drift/freshness checks from the indexer side and exits with a non-zero code when graph drift is too high for exact implementation targeting.

**Tasks:**

- Add `codemeridian index --verify` or `codemeridian verify`.
- Compare indexed file paths and line metadata against the current working tree.
- Report missing files, invalid line ranges, and missing timestamps.
- Recommend `codemeridian index . --project <ProjectName> --clear` when drift is moderate or high.
- Keep it fast enough for pre-work checks and CI.

**Effort:** Medium  
**Value:** High  
**Risk:** Low to medium, mostly around local path mapping when the MCP server runs remotely.

## - [x] P1 - Package the Indexers for Easier Use

**Why:** The old language-specific indexer commands worked for contributors, but they were not a polished user experience. Users should not need to know whether to run `tools/RoslynIndexer`, `tools/TsIndexer`, or another future language indexer; they should install one thing and run one command against a repo.

**Best target experience:**

```powershell
codemeridian index .
codemeridian index C:\Projects\MyApp --project MyApp --watch
codemeridian index . --clear
```

**Packaging options:**

- Publish `tools/Indexer` as a .NET global tool: `dotnet tool install -g CodeMeridian.Indexer`.
- Publish the TypeScript indexer as an npm package for JS-only environments.
- Keep `tools/Indexer` as the recommended unified CLI that dispatches to C#, TypeScript, docs, diagnostics, and future indexers.
- Add a Docker-based indexing option for CI or machines without local SDK setup.

**CLI improvements:**

- One command for C#, TypeScript, docs, diagnostics, and future HTML/CSS indexing.
- Auto-detect project type, package manager, solution file, `tsconfig.json`, ESLint config, and repo root.
- Clear installation docs for local, CI, and Docker usage.
- Stable exit codes for CI.
- `--dry-run` to show what will be indexed.
- `--list-capabilities` to show which indexers are available on the current machine.
- `--skip-csharp`, `--skip-typescript`, `--skip-docs`, `--skip-diagnostics` flags.

**Repository impact:**

- Keep language-specific indexers internally modular.
- Move shared CLI parsing/config/env loading into a common library if duplication grows.
- Ensure auth/server URL handling is consistent across all indexers.

**Effort:** Medium  
**Value:** High  
**Risk:** Medium, mostly around packaging, versioning, and cross-platform command execution.

## - [ ] P2 - Improve Cross-Language Connection Quality

**Why:** Current C# and TypeScript indexing share the graph, but true frontend-to-backend tracing needs stronger edges than imports and class relationships.

**Tasks:**

- Extract HTTP route endpoints from ASP.NET minimal APIs/controllers.
- Extract frontend fetch/axios calls and route strings.
- Link matching API calls to backend endpoint nodes.
- Surface these links in `find_connection` and `build_minimal_context`.

**Effort:** High  
**Value:** High for full-stack repos  
**Risk:** High, because route matching can be heuristic.

## - [ ] P2 - Add HTML / CSS / SCSS Relationship Indexing

**Why:** Frontend context is not only TypeScript. In many apps, the important relationship is between components, templates, class names, selectors, and style files. Indexing this would let CodeMeridian answer "what styles affect this element?" or "which templates use this class?" without loading the whole frontend.

**Start with the useful static subset:**

- HTML files: elements, IDs, class names, attributes, and template file nodes.
- CSS / SCSS files: selectors, class selectors, ID selectors, custom properties, imports, and rough rule locations.
- TSX / JSX: `className` string literals and simple template literals.
- Link HTML/TSX class usage to matching CSS/SCSS selector nodes.
- Link stylesheet imports to importing files.

**Suggested graph relationships:**

- `UsesClass`: template/component -> CSS class selector
- `DefinesSelector`: stylesheet -> selector
- `UsesId`: template/component -> ID selector
- `ImportsStyle`: component/file -> stylesheet
- `UsesCssVariable`: rule/template -> CSS custom property
- `DefinesCssVariable`: stylesheet/rule -> CSS custom property

**Do not attempt in the first version:**

- Full CSS cascade resolution
- Specificity conflict analysis
- Runtime class names from arbitrary expressions
- Framework-specific style scoping rules beyond simple, explicit patterns
- Complete SCSS mixin/function evaluation

**Possible later expansion:**

- CSS specificity and override warnings
- Dead CSS selector detection
- Component-to-style impact analysis
- Tailwind class extraction and config-aware lookup
- Angular/Vue/Svelte template support

**Effort:** High  
**Value:** Medium to high for frontend-heavy repos  
**Risk:** High if it tries to model the full cascade; medium if the first version stays static and relationship-focused.

## - [ ] P2 - Add CI-Friendly Context Reports

**Why:** CodeMeridian can produce PR context summaries without relying on an interactive assistant.

**Report ideas:**

- Changed nodes
- Impact radius
- Missing tests
- Hotspot/churn warnings
- Cross-project dependency changes
- Suggested review focus

**Effort:** Medium  
**Value:** Medium  
**Risk:** Low.

## - [x] P2 - Add Duplicate-Code Workflow

**Why:** Once embeddings work, CodeMeridian can turn `find_similar_nodes` into a practical duplicate-code review flow.

**Tasks:**

- Add `find_duplicate_candidates`.
- Group similar methods/classes by score.
- Filter by project, namespace, node type, and size.
- Exclude tests by default.
- Show refactor risk using callers and coverage.

**Effort:** Medium  
**Value:** Medium  
**Risk:** Medium.

**Implemented:** Added `find_duplicate_candidates` for embedded method/class nodes. It filters by project, namespace, node type, minimum line count, similarity threshold, and excludes tests by default. Results include grouped duplicate candidates with similarity, size, fan-in refactor risk, and direct test-caller coverage signals.

## - [ ] P3 - Add More External Concept Indexers

**Why:** `link_external_concept` is powerful, but manual linking limits adoption.

**Ideas:**

- Database schema importer
- OpenAPI importer
- Kafka/topic config importer
- Terraform/resource importer
- Docker Compose service importer

**Effort:** Medium to high  
**Value:** Medium  
**Risk:** Medium.

## Not Recommended Yet

- Full source-code retrieval tools by default. This weakens the token-saving story.
- A complex agent orchestration layer. Copilot already handles reasoning; CodeMeridian should stay factual.
- Large UI/dashboard work before `build_minimal_context` proves the core workflow.
- More graph algorithms before context-pack quality improves.

## Suggested Implementation Order

- [x] Fix local-function node ID collisions.
- [x] Add `ContextDetailLevel` and compact output conventions.
- [x] Implement `build_minimal_context` by composing existing repository/service queries.
- [x] Add token estimation to context output.
- [x] Add diagnostics indexing for C#, TypeScript, and ESLint using project-native configs.
- [x] Package the indexers for easier install and one-command usage.
- [x] Add optional embeddings to the indexers.
- [x] Add duplicate-code candidate workflow on top of embeddings.
- [x] Improve exact symbol resolution.
- [ ] Add index verification command.
- [ ] Add source snippet support with strict budgets.
- [ ] Improve cross-language HTTP endpoint linking.
- [ ] Add static HTML / CSS / SCSS relationship indexing.

## Product Positioning

CodeMeridian should lean into this promise:

> CodeMeridian turns your codebase graph into a context budget manager for AI coding tools.

The best features are the ones that help an assistant answer:

- What is the smallest context needed for this task?
- What will break if this changes?
- Which files are actually relevant?
- Is this small enough for a fast model?
- Where is the risk: callers, tests, churn, external systems, or architecture boundaries?
- What compiler, type-checker, or lint diagnostics already affect this change?
