# Add Token Cost Estimation

- Status: done
- Priority: P1
- Note: The pasted product idea is strongest when CodeMeridian can tell the assistant how much context a task likely needs


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

