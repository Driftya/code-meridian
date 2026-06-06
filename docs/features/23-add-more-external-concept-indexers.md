# Add More External Concept Indexers

- Status: pending
- Priority: P3
- Note: `link_external_concept` is powerful, but manual linking limits adoption.


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
- [x] Add source snippet support with strict budgets.
- [x] Add `codemeridian doctor`.
- [x] Add index verification and CI drift check.
- [ ] Add session usefulness evaluation.
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
