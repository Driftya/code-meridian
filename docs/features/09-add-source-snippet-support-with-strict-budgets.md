# Add Source Snippet Support With Strict Budgets

- Status: done
- Priority: P1
- Note: Most context packs should not include source, but sometimes a small method body or interface signature is the most efficient context.


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

**Implemented:** `build_minimal_context` now supports opt-in source snippets through `includeSourceSnippets`. Snippets are limited to the target and top-ranked direct dependencies, use the remaining `maxTokens` budget, include line numbers, and truncate with an explicit marker when needed. When no budget remains or source metadata is unavailable, the context pack reports why snippets were skipped instead of returning whole files.

