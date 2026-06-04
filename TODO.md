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

## - [ ] P1 - Add Token Cost Estimation

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

## - [ ] P1 - Add Complexity-Based Model Guidance

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

## - [ ] P1 - Improve Test Discovery and Coverage Context

**Why:** `find_coverage_gaps` is useful, but `build_minimal_context` needs better test relevance. A context pack should identify tests that call the target, nearby tests by namespace/file, and missing tests.

**Tasks:**

- Add a repository query for tests related to a node.
- Include direct test callers when available.
- Fall back to namespace/file-name similarity.
- Mark heuristic matches clearly.

**Effort:** Medium  
**Value:** High  
**Risk:** Medium, because test relationships can be incomplete without better call resolution.

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
- [ ] Add token estimation to context output.
- [x] Add diagnostics indexing for C#, TypeScript, and ESLint using project-native configs.
- [x] Package the indexers for easier install and one-command usage.
- [ ] Add optional embeddings to the indexers.
- [x] Add duplicate-code candidate workflow on top of embeddings.
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
