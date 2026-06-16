# Agent Capabilities

CodeMeridian can be used as a global or project-local context tool for AI coding agents.

This folder contains copyable capability packs for agents that support custom instructions, skills, subagents, prompts, or similar workflows.

The files in `docs/agent-capabilities/` are intentionally provider-neutral. They are not auto-installed. Copy the relevant file into the provider-specific location you use.

## Available Capabilities

| Capability | File | Purpose |
|---|---|---|
| Context Skill | `docs/capabilities/skills/codemeridian-context-skill.md` | Build minimal graph-grounded context before code work. |
| Refactor Skill | `docs/capabilities/skills/codemeridian-refactor-skill.md` | Plan safer refactors with impact, tests, and dependency risk. |
| Test Planning Skill | `docs/capabilities/skills/codemeridian-test-planning-skill.md` | Find relevant tests and coverage gaps before behavior changes. |
| Context Agent | `docs/capabilities/agents/codemeridian-context-agent.md` | Specialist agent for gathering CodeMeridian context. |
| Architecture Review Agent | `docs/capabilities/agents/codemeridian-architecture-review-agent.md` | Specialist reviewer for architecture, impact, tests, and quality risks. |

## Recommended Usage

Use the context skill before:

* implementing a feature
* refactoring code
* deleting code
* changing public APIs
* debugging unfamiliar behavior
* planning tests
* reviewing impact or architecture risk

Use the context agent when your provider supports specialist agents or subagents and you want a dedicated helper to gather CodeMeridian context before the main agent edits files.

## Suggested Provider Locations

These locations are examples only. Check your provider documentation before relying on automatic discovery.

| Provider / Tool                  | Suggested Use                                                                                                                               |
| -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| Claude Code skill                | Copy `codemeridian-context-skill.md` to `.claude/skills/codemeridian-context/SKILL.md` or `~/.claude/skills/codemeridian-context/SKILL.md`. |
| Claude Code agent                | Copy `codemeridian-context-agent.md` to `.claude/agents/codemeridian-context.md` or `~/.claude/agents/codemeridian-context.md`.             |
| GitHub Copilot                   | Copy the relevant guidance into `.github/copilot-instructions.md` or `AGENTS.md`.                                                           |
| Codex / ChatGPT-style tools      | Use the skill as a reusable prompt or capability file if the tool supports skills. Otherwise paste the content into the chat.               |
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
