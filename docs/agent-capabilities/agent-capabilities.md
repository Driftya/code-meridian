# Agent Capabilities

CodeMeridian can be used as a global or project-local context tool for AI coding agents.

This folder contains copyable capability packs for agents that support custom instructions, skills, subagents, prompts, or similar workflows.

The files in `meridian-agent-capabilities/` are intentionally provider-neutral when copied by `codemeridian init`. They are not tied to one assistant vendor. Copy the relevant file into the provider-specific location you use when a client expects its own folder layout.

## Available Capabilities

| Capability | File | Purpose |
|---|---|---|
| Context Skill | `docs/agent-capabilities/skills/codemeridian-context/SKILL.md` | Build minimal graph-grounded context before code work. |
| Refactor Skill | `docs/agent-capabilities/skills/codemeridian-refactor/SKILL.md` | Plan safer refactors with impact, tests, and dependency risk. |
| Test Planning Skill | `docs/agent-capabilities/skills/codemeridian-test-planning/SKILL.md` | Find relevant tests and coverage gaps before behavior changes. |
| Context Agent | `docs/agent-capabilities/agents/codemeridian-context-agent.md` | Specialist agent for gathering CodeMeridian context. |
| Architecture Review Agent | `docs/agent-capabilities/agents/codemeridian-architecture-review-agent.md` | Specialist reviewer for architecture, impact, tests, and quality risks. |

## Recommended Usage

Use the context skill before:

* implementing a feature
* refactoring code
* deleting code
* changing public APIs
* debugging unfamiliar behavior
* planning tests
* reviewing impact or architecture risk

When a feature likely follows an existing slice, pair the context skill with `find_implementation_patterns` so the agent sees reusable entry/service/repository/test shapes before editing.

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
