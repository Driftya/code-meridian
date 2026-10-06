# Agent Capabilities

CodeMeridian can be used as a global or project-local context tool for AI coding agents.

This folder contains copyable capability packs for agents that support custom instructions, skills, subagents, prompts, or similar workflows.

The files in `meridian-agent-capabilities/` are intentionally provider-neutral when copied by `codemeridian init`. They are not tied to one assistant vendor. Copy the relevant file into the provider-specific location you use when a client expects its own folder layout.

## Available Capabilities

| Capability | File | Purpose |
|---|---|---|
| Context Skill | `docs/agent-capabilities/skills/codemeridian-context/SKILL.md` | Build minimal graph-grounded context before code work. |
| Frontend Skill | `docs/agent-capabilities/skills/codemeridian-frontend/SKILL.md` | Route HTML/CSS/SCSS work through frontend-aware generic tools and bounded frontend-specific analysis. |
| Refactor Skill | `docs/agent-capabilities/skills/codemeridian-refactor/SKILL.md` | Plan safer refactors with impact, tests, and dependency risk. |
| Test Planning Skill | `docs/agent-capabilities/skills/codemeridian-test-planning/SKILL.md` | Find relevant tests and coverage gaps before behavior changes. |
| Human Cognitive Seed Skill | `docs/agent-capabilities/skills/human-cognitive-seed/SKILL.md` | Strengthen human-led reasoning and preserve justified durable change context with explicit provenance. |
| Context Agent | `docs/agent-capabilities/agents/codemeridian-context-agent.md` | Specialist agent for gathering CodeMeridian context. |
| Architecture Review Agent | `docs/agent-capabilities/agents/codemeridian-architecture-review-agent.md` | Specialist reviewer for architecture, impact, tests, and quality risks. |

## Recommended Usage

### Context Follows The Target Folder

The CodeMeridian agents and skills resolve project context from the supplied target, including a file or directory outside the current checkout. They inspect its owning Git/worktree or configured solution root and applicable local `meridian.json`, confirm fallback names against indexed evidence, and retain a separate root/context/freshness map for each relevant scope. Nested index roots can exist within one Git repository; an external path can also be relevant without being indexed.

For example, `C:\Repos\MyApi\src\Service.cs` may belong to context `MyApi`, while `C:\Repos\SharedLibraries\src\Validator.cs` belongs to `SharedLibraries`, as established by each root's configuration and graph evidence. Query each target in its own context, then inspect cross-project dependencies and exact endpoints. The global backend connection can be shared; a global default project name does not prove that both directories share an indexed context.

For .NET packages, preserve the consumer's installed version/framework and the producer's source-association status. Verified links support normal source traversal; current-source associations remain useful for navigation and potential impact without being described as the installed implementation. Supplied external paths are context for the task, not automatic authorization to edit or run build scripts in another repository.

### SQL Indexing And Tools

The context, refactor and test-planning skills and both agents recognize the opt-in PostgreSQL SQL indexer. They resolve repository context separately from dialect/logical database scope/search path, inspect file-owned declarations/references and shared database objects, and retain unresolved, partial and stale coverage limits. SQL parsing is offline; it does not execute migrations or connect to a database.

SQL facts use the existing `query_codebase`, `resolve_exact_symbol`, freshness, minimal/editing-context, impact/downstream and connection tools. Config and documentation tools help locate settings/decisions; test shields need source confirmation where SQL test links are absent. There is no separate SQL-only MCP tool suite, and method/class dead-code tools cannot certify unused database objects. `JoinsWith` supports structural connection analysis, not runtime call tracing. SQL and legacy C#/TypeScript table identities are not automatically unified.

Use `indexing.sql.enabled` and `codemeridian index` for authorized indexing; `--skip-sql`, `--dry-run`, `--list-capabilities` and `--no-incremental` control selection or reanalysis. See [SQL setup](../indexing.md#postgresql-sql-files) and [tested support and limits](../../tools/SqlIndexer/supports.md).

### Workflow Selection

Use the context skill before:

* implementing a feature
* refactoring code
* deleting code
* changing public APIs
* debugging unfamiliar behavior
* planning tests
* reviewing impact or architecture risk

When a feature likely follows an existing slice, pair the context skill with `find_implementation_patterns` so the agent sees reusable entry/service/repository/test shapes before editing.

Use the human cognitive seed skill for reasoning-heavy design, learning, strategy, interpretation, and consequential choices. It preserves the user's starting model, adds evidence and alternatives, scales challenge depth to the stakes, and returns value-dependent judgment to the user. When CodeMeridian exposes `record_change_context` and `get_change_context`, the skill may preserve one compact durable decision, constraint, limitation, assumption, or follow-up against an exact node for future changes. Provenance remains explicit, and only an exact user-approved summary may be marked confirmed. It should not activate or record memory for routine implementation or clerical work. CodeMeridian supplies graph-grounded evidence and attributed storage; the skill governs how an agent reasons with them.

The context, refactor, and test-planning skills may hand an exact indexed target
and verified evidence to the human cognitive seed challenge only when the user
explicitly requests interactive reasoning or learning. Those skills provide the
evidence; the human cognitive seed skill owns challenge behavior. Ordinary
implementation, cleanup, and test work should continue directly without a quiz.

Use the frontend skill when the task touches HTML, CSS, SCSS, selectors, style imports, or CSS variables. It keeps the default routing generic by preferring `build_minimal_context`, `find_connection`, `find_impact`, and `find_implementation_surface` before using frontend-only analysis such as `find_frontend_cascade_conflicts`.

Use the context agent when your provider supports specialist agents or subagents and you want a dedicated helper to gather CodeMeridian context before the main agent edits files.

## When To Add More

Do not add a new skill or agent only because CodeMeridian has more tools.

Add a new capability only when there is a distinct, repeated workflow that the existing context, refactor, test-planning, or architecture-review capabilities do not cover. Prefer updating the existing capability routing when a new tool improves an existing workflow.

Good reasons to add a capability:

* a workflow has a different trigger and output contract
* the agent needs a different role boundary, such as investigation versus review
* the guidance would make an existing skill too broad or hard to follow

Prefer updating existing capabilities for:

* new graph tools that fit existing trigger rules
* reusable pattern-finding tools such as `find_implementation_patterns` that strengthen feature planning without changing the workflow boundary
* better ordering between current tools
* clearer freshness, impact, test, or documentation checks
* provider-specific placement notes

## Suggested Provider Locations

These locations are examples only. Check your provider documentation before relying on automatic discovery.

| Provider / Tool                  | Suggested Use                                                                                                                               |
| -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| Claude Code skill                | Copy `skills/codemeridian-context/` to `.claude/skills/codemeridian-context/` or `~/.claude/skills/codemeridian-context/`. |
| Claude Code agent                | Copy `codemeridian-context-agent.md` to `.claude/agents/codemeridian-context.md` or `~/.claude/agents/codemeridian-context.md`.             |
| GitHub Copilot                   | Copy the relevant guidance into `.github/copilot-instructions.md` or `AGENTS.md`.                                                           |
| Codex skills                     | Copy skill folders to `.agents/skills/` for repo-scoped skills or `$HOME/.agents/skills/` for user-scoped skills.                          |
| Codex agents                     | Run `install-codex-agents.ps1` to generate `.toml` custom agents under `.codex/agents/` or `$HOME/.codex/agents/` from the neutral markdown. |
| ChatGPT-style tools              | Use the skill as a reusable prompt or capability file if the tool supports skills. Otherwise paste the content into the chat.               |
| Continue / Cursor / other agents | Use the skill or agent text as custom instructions where supported.                                                                         |

## Source of Truth

For this repository, keep `AGENTS.md` as the primary source of project-wide agent behavior.

Capability files should stay focused on reusable CodeMeridian workflows, not duplicate the full repository contribution guide.

## Design Rules

Capability files should:

* be short enough that agents can follow them reliably
* use trigger-based instructions
* prefer minimal context over large file dumps
* require graph freshness checks when exactness matters
* separate proven graph relationships from inferred relationships
* tell the user when CodeMeridian data is stale, incomplete, or missing

Capability files should not:

* require provider-specific tools unless clearly marked
* duplicate large sections from `README.md`, `CONTRIBUTING.md`, or `AGENTS.md`
* ask agents to trust graph data blindly
* encourage broad repository scans before trying graph lookup
