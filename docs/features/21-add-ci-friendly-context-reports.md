# Add CI-Friendly Context Reports

- Status: done
- Priority: P2
- Type: CLI-first, MCP-compatible
- Note: CodeMeridian can produce deterministic PR context summaries without relying on an interactive assistant.


**Why:** CodeMeridian can produce PR context summaries without relying on an interactive assistant.

**Primary interface:** `codemeridian report pr-context`

**Current outputs:**

- Markdown to stdout or `--output`
- JSON to stdout or `--output`

**Report ideas:**

- Changed nodes
- Impact radius
- Missing tests
- Hotspot/churn warnings
- Cross-project dependency changes
- Suggested review focus
- Related documentation

**Implemented:**

- Added `codemeridian report pr-context --base <ref> --head <ref> --format markdown|json [--output <path>] [--include-docs]`.
- The CLI computes changed files from git diff locally, then calls the backend for deterministic graph-backed report data.
- Added an Application `PrContextReportService` so the core report logic stays outside the CLI adapter.
- Added a backend `/api/v1/status/report/pr-context` endpoint and SDK support for the new report request/response.
- Related docs are ranked from changed files and changed graph nodes using the existing keyword extraction rules, then filtered to higher-confidence Markdown matches.
- The current report includes changed files, changed nodes, impact radius, missing-test warnings, hotspot/churn warnings, related documentation, and suggested review focus.
- Published callable definitions/dependencies, test-to-code, and change-history coverage are required. `includeDocs` additionally requires document-search coverage; disabling it skips document reads and the Markdown documentation section.
- API/SDK responses add nullable `applicability` with version, status, project, generation, scope, coverage, reasons, limitations, and next actions. Production reports check readiness before queries and again afterward, discarding stale results.
- The CLI renders unavailable coverage explicitly and returns exit code 2 while still writing the requested output artifact. Successful reports return 0; transport/Git errors return 1. Responses from older servers without `applicability` retain their existing rendering and exit behavior.

**Notes:**

- This first slice keeps the user-facing feature CLI-first. MCP can wrap the same report service later without changing the report logic.
- Related-doc matching uses deterministic keyword scoring and does not require an interactive assistant session.

**Effort:** Medium  
**Value:** Medium  
**Risk:** Low.

