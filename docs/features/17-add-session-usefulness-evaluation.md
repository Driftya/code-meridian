# Add Session Usefulness Evaluation

- Status: implemented
- Priority: P1
- Note: CodeMeridian should be able to answer whether it actually helped an implementation session


**Why:** CodeMeridian should be able to answer whether it actually helped an implementation session. This directly tests the product promise: did the graph reduce search and point at the right files/tests, or did the assistant mostly fall back to manual exploration?

**Suggested command:**

```powershell
codemeridian evaluate-session --project MyApp
```

Implemented as:

```powershell
codemeridian evaluate-session . --project MyApp --session .meridian/sessions/session.jsonl --base HEAD
```

If `--session` is omitted, CodeMeridian reads the newest `.meridian/sessions/*.jsonl` file. `--base` controls the git diff base used to detect files edited during the session.

**Provider-neutral evidence format:**

Session evidence is newline-delimited JSON so any client or importer can write it, including Copilot, Codex, Claude, Continue, or a custom transcript converter. CodeMeridian evaluates facts, not provider-specific reasoning.

```json
{"project":"MyApp","provider":"codex","kind":"graph-call","toolName":"mcp__CodeMeridian.find_implementation_surface","files":["src/App/OrderService.cs"],"tests":["tests/App.Tests/OrderServiceTests.cs"],"targetConfidence":"exact"}
{"project":"MyApp","provider":"codex","kind":"command","command":"rg -n \"Order\" src tests"}
{"project":"MyApp","provider":"codex","kind":"test-run","command":"dotnet test tests/App.Tests","tests":["tests/App.Tests/OrderServiceTests.cs"]}
{"project":"MyApp","provider":"codex","kind":"stale-warning"}
```

Useful event fields:

- `kind`: `graph-call`, `codemeridian-tool`, `suggestion`, `tool-result`, `command`, `manual-fallback`, `test-run`, or `stale-warning`.
- `toolName`: CodeMeridian MCP tool name when the event came from graph lookup.
- `files`: files suggested by CodeMeridian.
- `tests`: tests suggested or run.
- `targetConfidence`: comma-separated confidence labels such as `exact`, `file-only`, `heuristic`, or `stale`.
- `staleWarning`: `true` when a tool result warned about graph freshness.

No separate git CLI is required. Git is treated as one evidence adapter inside `evaluate-session`, currently using `git diff --name-only --diff-filter=ACMRTUXB <base> --` to compare suggested files with changed files.

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

- Start simple with a local session log written by MCP tools, importers, agents, or the indexer.
- Track tool calls, returned file paths, returned node IDs, confidence labels, and stale/drift warnings.
- Compare against `git diff --name-only` and test command history when available.
- Keep the score explainable; do not hide it behind a vague metric.

**Current behavior:**

- Rates sessions as `high`, `partial`, `low`, or `unknown`.
- Reports suggested files edited, suggested tests changed/run, graph calls, confidence counts, stale warnings, and manual fallback commands.
- Counts `build_minimal_context` results recorded as `contextPackStatus: full|degraded|failed` so context-pack degradation can be tracked separately from hard failures.
- Keeps the format provider-neutral so integrations can be added as importers instead of hardcoding Copilot/Codex/Claude transcript formats into the evaluator.

**Effort:** Medium to high  
**Value:** High  
**Risk:** Medium, because editor/agent sessions differ and not every client exposes command history.

