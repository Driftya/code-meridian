# Evaluating Session Usefulness

Use `codemeridian evaluate-session` after an implementation session to check whether CodeMeridian actually helped. The command compares provider-neutral session evidence with files changed in git and reports an explainable usefulness result.

This works with Codex, GitHub Copilot, Claude Code, Continue, Cline, or any other agent workflow that can write or export JSONL facts.

## 1. Start From An Indexed Project

Run CodeMeridian normally before the implementation session:

```powershell
codemeridian serve
codemeridian init .
codemeridian index . --project MyApp
```

If you are working from this repository source checkout:

```powershell
dotnet run --project tools/Indexer -- . --project CodeMeridian
```

## 2. Record Session Evidence

Create a session evidence file under `.meridian/sessions/`. The file is newline-delimited JSON, with one event per line:

```powershell
New-Item -ItemType Directory -Force .meridian/sessions
New-Item -ItemType File .meridian/sessions/session.jsonl
```

Agents or transcript importers should append facts using this schema:

```json
{
  "timestamp": "2026-06-17T12:34:56Z",
  "provider": "codex",
  "project": "MyApp",
  "kind": "graph-call",
  "toolName": "mcp__CodeMeridian.find_implementation_surface",
  "command": null,
  "targetConfidence": "exact",
  "staleWarning": false,
  "files": ["src/App/OrderService.cs"],
  "tests": ["tests/App.Tests/OrderServiceTests.cs"]
}
```

Each JSON object must be written on one line in the `.jsonl` file. Omit fields that do not apply, but keep `kind` accurate because it controls how the evaluator counts the event.

Common event shapes:

```json
{"project":"MyApp","provider":"codex","kind":"graph-call","toolName":"mcp__CodeMeridian.find_implementation_surface","files":["src/App/OrderService.cs"],"tests":["tests/App.Tests/OrderServiceTests.cs"],"targetConfidence":"exact"}
{"project":"MyApp","provider":"codex","kind":"command","command":"rg -n \"Order\" src tests"}
{"project":"MyApp","provider":"codex","kind":"test-run","command":"dotnet test tests/App.Tests","tests":["tests/App.Tests/OrderServiceTests.cs"]}
{"project":"MyApp","provider":"codex","kind":"stale-warning"}
{"project":"MyApp","provider":"codex","kind":"tool-result","toolName":"mcp__CodeMeridian.build_minimal_context","contextPackStatus":"degraded","files":["src/App/OrderService.cs"],"tests":["tests/App.Tests/OrderServiceTests.cs"]}
```

The evaluator does not require provider-specific transcript formats. Keep the evidence factual: tool called, files suggested, tests suggested or run, confidence labels, stale warnings, and fallback commands.

## 3. Run The Evaluation

From the repository root:

```powershell
codemeridian evaluate-session . --project MyApp --session .meridian/sessions/session.jsonl
```

If `--session` is omitted, CodeMeridian reads the newest `.meridian/sessions/*.jsonl` file:

```powershell
codemeridian evaluate-session . --project MyApp
```

Use `--base` when your session started from a different git ref:

```powershell
codemeridian evaluate-session . --project MyApp --base origin/main
```

## 4. Read The Result

Example output:

```text
CodeMeridian usefulness: partial
Session evidence: C:\Projects\MyApp\.meridian\sessions\session.jsonl
Suggested files edited: 4/6
Suggested tests changed/run: 2/3
Graph calls used: 5
Exact targets used: 3
File-only targets: 1
Heuristic targets verified manually: 2
Stale targets: 0
Stale warnings: 1
Manual fallback commands after graph lookup: 14
Context packs: full 2, degraded 1, hard failure 0
```

Ratings:

- `high`: CodeMeridian suggestions mostly matched edited files and tests, with little manual fallback and no stale warning pressure.
- `partial`: Some graph evidence matched the work, but the session still needed fallback search, stale checks, or only partial target matches.
- `low`: CodeMeridian was used, but its suggestions did not match the implementation work.
- `unknown`: The session evidence did not include enough CodeMeridian facts to evaluate usefulness.

## Evidence Fields

Useful JSONL fields:

- `timestamp`: optional ISO 8601 timestamp for when the event happened.
- `project`: project context name. Unscoped events are included in every project evaluation.
- `provider`: optional agent or importer name, such as `codex`, `copilot`, `claude`, or `continue`.
- `kind`: `graph-call`, `codemeridian-tool`, `suggestion`, `tool-result`, `command`, `manual-fallback`, `test-run`, or `stale-warning`.
- `toolName`: CodeMeridian MCP tool name, such as `mcp__CodeMeridian.find_implementation_surface`.
- `files`: files CodeMeridian suggested.
- `tests`: tests CodeMeridian suggested or the session ran.
- `command`: shell command run during the session.
- `targetConfidence`: comma-separated confidence labels such as `exact`, `file-only`, `heuristic`, or `stale`.
- `staleWarning`: `true` when a tool result warned that graph data may be stale.
- `contextPackStatus`: for `build_minimal_context` result events, record `full`, `degraded`, or `failed` so the evaluator can count bounded success separately from hard failure.

Unknown fields are allowed. Importers can preserve provider-specific metadata without breaking the evaluator.

## What The Evaluator Counts

The evaluator uses these rules:

- A graph call is counted when `kind` is `graph-call` or `codemeridian-tool`, or when `toolName` starts with `mcp__CodeMeridian.` or `CodeMeridian.`.
- Suggested files and tests are read from `files` and `tests` on graph call, suggestion, or tool-result events.
- Context-pack outcomes are counted from `contextPackStatus` on `build_minimal_context` result events.
- Manual fallback commands are counted when `kind` is `manual-fallback`, or when `kind` is `command` and `command` starts with `rg`, `grep`, `find`, `Get-ChildItem`, or `Select-String`.
- Test runs are counted when `kind` is `test-run`, or when `kind` is `command` and `command` contains common test runners such as `dotnet test`, `npm test`, `pnpm test`, `yarn test`, `vitest`, or `pytest`.
- Stale warnings are counted when `kind` is `stale-warning` or `staleWarning` is `true`.
- Target confidence counts come from `targetConfidence`, split by comma.

## What Git Is Used For

`evaluate-session` uses git only to identify changed files:

```text
git diff --name-only --diff-filter=ACMRTUXB <base> --
```

There is no separate CodeMeridian git wrapper. Git is one evidence source behind the evaluator, which keeps the workflow generic across LLM providers.

## Recommended Agent Prompt

Use a prompt like this at the start of a session:

```text
Use CodeMeridian before implementation. Record session evidence as newline-delimited JSON under .meridian/sessions/session.jsonl.

Use this event schema:
{"timestamp":"<ISO-8601 UTC time>","provider":"<codex|copilot|claude|continue|other>","project":"MyApp","kind":"<graph-call|codemeridian-tool|suggestion|tool-result|command|manual-fallback|test-run|stale-warning>","toolName":"<CodeMeridian MCP tool name when applicable>","command":"<shell command when applicable>","targetConfidence":"<exact|file-only|heuristic|stale, comma-separated if needed>","staleWarning":<true|false>,"contextPackStatus":"<full|degraded|failed when recording build_minimal_context results>","files":["<repo-relative file path>"],"tests":["<repo-relative test file path>"]}

Write one compact JSON object per line. Omit fields that do not apply. For each CodeMeridian tool call, record kind=graph-call, toolName, files suggested by the tool, tests suggested by the tool, targetConfidence, and staleWarning when present. When recording a build_minimal_context result as a tool-result event, also record contextPackStatus as full, degraded, or failed. For manual search fallback commands such as rg, grep, find, Get-ChildItem, or Select-String, record kind=command and command. For test execution, record kind=test-run, command, and tests when known.
```

After the work is done, run:

```powershell
codemeridian evaluate-session . --project MyApp
```
