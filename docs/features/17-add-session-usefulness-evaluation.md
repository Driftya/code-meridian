# Add Session Usefulness Evaluation

- Status: pending
- Priority: P1
- Note: CodeMeridian should be able to answer whether it actually helped an implementation session


**Why:** CodeMeridian should be able to answer whether it actually helped an implementation session. This directly tests the product promise: did the graph reduce search and point at the right files/tests, or did the assistant mostly fall back to manual exploration?

**Suggested command:**

```powershell
codemeridian evaluate-session --project MyApp
```

**Signals to compare:**

- Files CodeMeridian suggested.
- Files actually edited.
- Tests CodeMeridian suggested.
- Tests actually changed or run.
- Stale warnings emitted.
- Graph calls used.
- Whether exact, file-only, heuristic, or stale targets were returned.

**Example output:**

```text
CodeMeridian usefulness: partial
Suggested files edited: 4/6
Suggested tests changed/run: 2/3
Exact targets used: 3
Heuristic targets verified manually: 2
Stale warnings: 1
Manual fallback commands after graph lookup: 14
```

**Implementation options:**

- Start simple with a local session log written by MCP tools and the indexer.
- Track tool calls, returned file paths, returned node IDs, confidence labels, and stale/drift warnings.
- Compare against `git diff --name-only` and test command history when available.
- Keep the score explainable; do not hide it behind a vague metric.

**Effort:** Medium to high  
**Value:** High  
**Risk:** Medium, because editor/agent sessions differ and not every client exposes command history.

