# Human Cognitive Seed Capability Plan

- Status: implemented and verified
- Date: 2026-08-10
- Scope: add a provider-neutral, trigger-scoped agent skill that strengthens human reasoning without adding friction to routine work
- Distribution: package the skill through the existing `codemeridian init` agent-capability bundle
- Architectural boundary: CodeMeridian supplies deterministic evidence; client-owned capability instructions govern epistemic behavior

## Problem

AI can remove cognitive drudgery, but it can also silently replace the reasoning through which a user builds judgment and understanding. A permanent instruction to ask what the user thinks first would overcorrect: it would interrupt formatting, boilerplate, lookup, ordinary implementation, and other routine tasks.

The capability therefore needs a precise trigger and proportional behavior. It should think with the user when reasoning matters and disappear when direct execution is more useful.

## Decisions

1. Implement reasoning behavior as a provider-neutral skill, not an MCP reasoning tool. The server remains responsible for deterministic facts and attributed storage rather than agent personality or hidden reasoning policy.
2. Prefer trigger precision over recall. False positives are more damaging than occasional missed activation because repeated friction would cause users to disable the capability.
3. Use four modes:
   - Direct for mechanical or explicitly requested execution.
   - Scaffold for expanding and challenging an existing human model.
   - Teach for productive struggle when mastery is the goal.
   - Deliberate for consequential decisions with explicit trade-offs, uncertainty, and a credible counter-model.
4. Scale challenge depth from light to deep according to stakes, reversibility, and uncertainty.
5. Make the observable contract semantic rather than structural. Responses must preserve the reasoning loop without forcing recurring headings.
6. Protect coherent unconventional ideas from automatic normalization toward the statistically common answer. Evaluate them against evidence and constraints first.
7. Distinguish known, inferred, speculative, preference-dependent, and unknown claims when the distinction matters.
8. Store durable context as a separate `KnowledgeDocument:HumanCognitiveSeedContext` linked to one exact `CodeNode` through `Mentions`; never write LLM or user text into canonical code-node properties.
9. Treat provenance as reported and unverified. Only `user-approved` plus `userConfirmed=true` represents explicit approval of the exact stored summary.
10. Keep retrieval opt-in and bounded. Ordinary document listing, counting, text search, vector search, and editing context must not include cognitive-seed records automatically.

## Implementation

- [x] Create `docs/agent-capabilities/skills/human-cognitive-seed/SKILL.md`.
- [x] Add optional client metadata at `docs/agent-capabilities/skills/human-cognitive-seed/agents/openai.yaml`.
- [x] Add the skill and its activation boundary to the agent-capability catalog.
- [x] Rely on the existing recursive `WriteAgentCapabilities` implementation and Indexer wildcard packaging; do not add production code.
- [x] Extend the direct distribution test to require the new `SKILL.md` and client metadata.
- [x] Validate the skill structure.
- [x] Run the focused packaging and init tests.
- [x] Exercise the Codex skill installer against a temporary destination and confirm discovery and installation.
- [x] Add focused Core and Application contracts for durable change context.
- [x] Add an idempotent Neo4j repository that writes a separate knowledge node and weak `Mentions` edge.
- [x] Add `record_change_context` and `get_change_context` MCP tools.
- [x] Capture exact-node project, source-hash, and update-time provenance at write time.
- [x] Add unchanged, changed, hash-unknown, and orphaned read status.
- [x] Exclude cognitive-seed records from ordinary document queries by default.
- [x] Integrate recording and retrieval rules into the human-cognitive-seed skill.
- [x] Run focused Application, Infrastructure, MCP, Indexer, and skill validation.
- [x] Run the full .NET regression suite.

## Durable Context Contract

`record_change_context` accepts one exact existing node, a statement of at most 800 characters, a bounded context kind, reported provenance, explicit confirmation state, and an optional idempotency key. It returns a compact receipt without echoing the statement.

`get_change_context` returns at most ten records and labels every statement as attributed, unverified memory. It compares the stored source hash with the current target and reports `graph-unchanged-since-context`, `target-changed-since-context`, `hash-unknown`, or `orphaned` without claiming the statement is true.

Supported context kinds are `decision`, `constraint`, `limitation`, `assumption`, and `follow-up`. Supported provenance values are `agent-synthesized`, `user-stated`, and `user-approved`.

## Behavioral Acceptance Scenarios

The capability should:

- preserve and challenge a user's software-design hypothesis;
- perform a formatting or boilerplate request directly;
- give a learner a useful hint before a solution when mastery is the stated goal;
- provide the answer when a blocked user explicitly requests it;
- include a credible counter-model for a consequential decision;
- distinguish evidence from inference when uncertainty matters;
- avoid a long questionnaire when the user has not supplied an initial model; and
- preserve a coherent unconventional seed until evidence or constraints contradict it.

## Verification

Run the narrowest checks first:

```powershell
python C:\Users\josh.gomez\.codex\skills\.system\skill-creator\scripts\quick_validate.py `
  docs\agent-capabilities\skills\human-cognitive-seed

dotnet test tests\CodeMeridian.Indexer.Tests\CodeMeridian.Indexer.Tests.csproj `
  --filter "FullyQualifiedName~WriteAgentCapabilities|FullyQualifiedName~InitCommand"

dotnet test tests\CodeMeridian.Application.Tests\CodeMeridian.Application.Tests.csproj `
  --filter "FullyQualifiedName~HumanCognitiveSeedContextService"

dotnet test tests\CodeMeridian.McpServer.Tests\CodeMeridian.McpServer.Tests.csproj

dotnet test tests\CodeMeridian.Infrastructure.Integration.Tests\CodeMeridian.Infrastructure.Integration.Tests.csproj `
  --filter "FullyQualifiedName~Neo4jChangeContextRepository"

pwsh scripts\meridian-agent-capabilities\codex-scripts\install-codex-skills.ps1 `
  -SourceRoot docs\agent-capabilities\skills `
  -DestinationRoot <temporary-directory> `
  -List
```

Run the full .NET suite only if focused verification reveals shared packaging or init behavior changes.

### Verification Record

- `quick_validate.py`: skill is valid. PyYAML was supplied in an isolated temporary directory because the active Python environment did not include it.
- Focused Indexer tests: 8 passed, 0 failed, 0 skipped.
- Focused Application tests: 4 passed, 0 failed, 0 skipped.
- Focused Neo4j integration tests: 1 passed, 0 failed, 0 skipped.
- MCP contract and wrapper suite: 122 passed, 0 failed, 6 environment-gated live tests skipped.
- Full solution regression: 1,040 passed, 0 failed, 6 environment-gated live tests skipped.
- Installer listing: `human-cognitive-seed` was discovered with the other bundled skills.
- Temporary installation: `SKILL.md` and `agents/openai.yaml` were both installed successfully.
- Temporary validator dependencies and installation output were removed after verification.
- `git diff --check`: passed.

## Success Criteria

- The skill activates for clear reasoning, learning, or consequential-decision tasks and stays inactive for routine work.
- The response behavior preserves the user's model, adds useful evidence or alternatives, challenges proportionally, and returns human judgment without a rigid template.
- `codemeridian init` copies the complete skill through the existing capability bundle.
- The Codex installer lists and installs the skill from the bundled directory.
- Durable context remains separate from canonical `CodeNode` facts and is never injected into ordinary context automatically.
- MCP mutation and retrieval contracts are bounded, attributed, idempotent where applicable, and schema-tested.
- Focused and full regression tests pass without crossing Clean Architecture boundaries.
