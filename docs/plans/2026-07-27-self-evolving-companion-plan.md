# Meridian Evolution: Standalone Cognitive Ledger Plan

- Status: standalone cognitive-mind foundation complete; advanced learning/action roadmap remains active
- Date: 2026-07-27
- Working name: Meridian Evolution
- Scope: an entirely standalone cognitive ledger around one or more LLMs that observes, remembers, maintains continuity, pursues governed goals, projects its internal state to humans, improves its skills and software, and learns from verified outcomes
- Size rationale: this is a cross-cutting research and architecture roadmap; headings keep each concern independently reviewable

## Assumptions

- This is a self-contained solution under `companions/CodeMeridian.Evolution/` with its own application, database, UI, build, tests, identity, and deployment boundary.
- Physical co-location is for incubation and shared contributor convenience; it does not create runtime coupling to the root CodeMeridian solution.
- The companion must boot, operate, evolve, and pass its core acceptance tests without CodeMeridian installed or reachable.
- The companion owns its complete ledger, sensors, memory, identity continuity, goals, orchestration, prompts, skills, experiments, UI, and improvement history.
- "Self-evolving" means continuously learning at the system level and proposing evaluated changes. It does not mean silently changing production or continuously modifying model weights.
- The first release builds and improves Meridian Evolution itself. Its UI exists only to let humans observe, understand, question, correct, and govern the companion.
- CodeMeridian is an optional external code-intelligence integration. Meridian Evolution may use it to understand and improve its own source code or independently suggest improvements to CodeMeridian.
- A human must approve repository writes, pull requests, deployments, and production rollouts until the system has earned a narrower, explicitly configured autonomy level.
- The project may investigate machine consciousness, but it must not claim subjective experience, sentience, feelings, or moral status from behavior alone.

## Clarified Entity Model

Meridian Evolution is not merely CodeMeridian's audit ledger. It is the persistent cognitive
entity: its ledger is the substrate for identity, memory, affect-like regulation, drives, goals,
attention, simulations, approvals, and learning history.

CodeMeridian is a separate entity with two bounded relationships:

1. Evolution may consume authenticated, read-only CodeMeridian graph evidence to understand either
   repository.
2. Evolution may create a CodeMeridian-attributed improvement simulation and candidate for human
   review.

Evolution never inherits CodeMeridian's identity or writes its cognitive state into CodeMeridian.
The shipped system stops after human candidate approval. A future model-driven writer must operate
in an OS-isolated disposable workspace, may use CodeMeridian evidence to prepare a patch, and must
remain unable to merge, publish, deploy, broaden scope, or approve itself.

The “human-mind” analogy is functional: sensorium, global attention, persistence, motivation,
prediction error, reward/decay, simulation, and an executive loop. It is not an implementation or
claim of biological equivalence, phenomenal consciousness, pleasure, distress, or moral status.

## Implementation Snapshot

As of 2026-07-28, the standalone cognitive-mind foundation is implemented:

- the companion has its own solution, runtime composition, PostgreSQL database, Compose stack,
  React control plane, tests, docs, identity, and deployment boundary
- immutable cognitive transactions, evidence, postings, corrections, sequence and hash validation,
  trial balance, and independently rebuilt projections form the durable ledger
- in-memory and PostgreSQL stores implement ordered, atomic, idempotent append; PostgreSQL uses a
  serializable transaction and advisory lock to prevent concurrent forks
- governed goal intake, goal pause, global pause/resume, human correction, observation admission,
  and bounded reasoning records are executable through the API and UI
- affect-like state, dopamine/curiosity signals, homeostatic decay, derived drives, deterministic
  project-specific attention, mental simulation, and recurring cognitive cycles form a functional
  mind loop without asserting subjective feeling
- lifecycle, resource, ledger-integrity, human-prompt, allowlisted internet-feed, and optional
  CodeMeridian graph sensors run with per-sensor failure handling, trust labels, project
  attribution, size limits, and idempotent observation keys
- the deterministic fake provider and configurable OpenAI-compatible chat provider are
  capability-probed, read-only, cancellable, and time-bounded; evidence is labeled untrusted and no
  hidden chain-of-thought is requested or stored
- CodeMeridian and Meridian Evolution are explicit separate project entities; Evolution may use
  CodeMeridian graph diagnostics as evidence without moving its ledger, identity, or runtime into
  CodeMeridian
- a non-abstaining cycle records an inspectable, side-effect-free simulation and a pending
  project-attributed change candidate; only a separate human approval event can reconcile it
- the standalone UI projects Now, Identity, Mind, Ledger, Memory, Goals, Sensors, Reasoning,
  Evolution, Dialogue, and Governance without a runtime CodeMeridian or external-font dependency
- Domain, Application, Infrastructure, integration, API, Worker, architecture, and UI tests protect
  the v1 acceptance path, including restart/replay continuity and adjusting-entry correction
- the constitution, operational definitions, threat model, authority matrix, acceptance scenarios,
  local runbook, and container deployment are checked in with the companion

The word `complete` applies to this functional cognitive-mind foundation. It now senses prompts and
optional external feeds, calls a configured model, remembers, prioritizes, simulates, changes
functional motivation, and produces governed improvement candidates. Semantic memory
consolidation, an OS-isolated repository-writing adapter, action rollback, autonomous
publishing/deployment, learned-skill promotion, adaptive routing, model-weight adaptation, and
consciousness conclusions are not shipped capabilities.

## Product Thesis

An LLM resembles a powerful but frozen cognitive organ:

- its trained weights normally do not change during use
- each invocation begins without durable personal continuity
- its working context is finite
- it has no native sensorium, body, clock, durable goals, or independent action loop
- it cannot reliably distinguish its knowledge from its guesses
- it does not automatically consolidate experience into stable memory or reusable skills

Meridian Evolution supplies the missing persistent machinery around the model. Its center is an accounting-inspired cognitive ledger: an auditable history of what entered the system, what it believed, attended to, intended, did, changed, learned, and still owes. The goal is not to pretend that a wrapper magically creates consciousness. The goal is to construct and measure the functional ingredients associated with an enduring cognitive agent:

- continuous perception
- attention and working memory
- episodic and semantic memory
- a stable but revisable self-model
- temporal continuity
- goals and priorities
- reflection and metacognition
- planning and action
- skill acquisition
- learning from consequences
- social and ethical constraints

This produces a standalone research and operational platform for increasingly coherent, adaptive, self-directed behavior. Whether such a system has subjective experience is a separate scientific and philosophical question that the software cannot settle by self-report.

## Product Goal

Build a persistent cognitive ledger and control plane that can:

1. register pluggable sensors
2. collect normalized observations
3. maintain a bounded, evidence-backed model of its environment and itself
4. preserve relevant memories and continuity across model calls
5. create, prioritize, suspend, and revisit governed goals
6. plan and act through registered tools
7. reflect on outcomes and uncertainty
8. acquire reusable skills without immediately changing base-model weights
9. propose bounded software or policy improvements
10. validate changes against declared success and guardrail metrics
11. ask for approval at important boundaries
12. consolidate verified outcomes so future behavior improves

The system should be able to explain:

- what it observed
- what it remembers and why that memory was retrieved
- what it currently believes, including uncertainty and provenance
- which goals are active and where they came from
- why the observation matters
- which evidence supports the proposal
- what it wants to change
- how the change will be tested
- what happened after the change
- what it learned

## Cognitive Ledger Model

Use bookkeeping as the architectural metaphor without forcing every cognitive value into financial debit and credit columns.

### Journal

The journal is an immutable chronological stream of:

- observations received
- attention selections
- memories admitted, retrieved, consolidated, challenged, expired, or forgotten
- beliefs created, strengthened, weakened, contradicted, or retired
- goals proposed, authorized, prioritized, suspended, completed, failed, or cancelled
- commitments made, discharged, breached, transferred, or waived
- plans considered and decisions selected
- actions requested, approved, attempted, completed, failed, or reversed
- provider invocations and bounded decision records
- evaluations, reflections, skills, policy changes, and learning outcomes
- human corrections, approvals, objections, and interpretations

Journal events are never edited in place. Corrections use adjusting entries that reference the incorrect event and preserve both history and current interpretation.

### Ledgers

Project journal events into independently rebuildable ledgers:

- identity ledger: who the agent is authorized to be and how that changed
- belief ledger: propositions, provenance, confidence, freshness, and contradictions
- memory ledger: episodes, semantic consolidation, retrieval, forgetting, and privacy state
- goal ledger: goals, priorities, dependencies, budgets, and completion evidence
- commitment ledger: promises and obligations to humans, systems, and itself
- authority ledger: constitutions, policies, permissions, approvals, and revocations
- attention ledger: what entered the global workspace and why
- action ledger: tool requests, side effects, results, reversals, and idempotency
- skill ledger: procedural versions, tests, uses, outcomes, and retirement
- relationship ledger: consent, preferences, trust boundaries, and interaction history
- resource ledger: time, tokens, money, compute, storage, and rate limits
- research ledger: consciousness hypotheses, protocols, evidence, counterevidence, and reviews

### Cognitive transaction

A state-changing transaction is atomic and contains:

- transaction id, time, actor, and causal parent
- source observations and evidence
- affected ledger accounts
- prior-state hashes and proposed postings
- authority and approval references
- uncertainty and alternative interpretations
- expected effects and guardrails
- actual outcome and reconciliation status
- compensating or reversal transaction when needed

"Balanced" means that every durable state claim has provenance, every authorized action has an accountable actor and outcome, and every correction preserves lineage. It does not mean inventing meaningless numeric equality for beliefs or memories.

### Trial balance and reconciliation

Run continuous consistency checks:

- active goals agree with authority and budget ledgers
- commitments have owners, due states, and resolution paths
- self-model capability claims agree with current evaluations
- semantic memories resolve or expose contradictory evidence
- action effects agree with external observations
- resource use agrees with provider and infrastructure records
- current projections can be rebuilt from the journal
- no material state exists only inside an LLM context or provider session

Reconciliation compares internal records with external reality. Unreconciled items remain visible and cannot silently become trusted memory.

### Closing and consolidation

At bounded intervals, create signed cognitive-period summaries:

- important events and unresolved items
- changes in beliefs, goals, skills, relationships, and self-model
- performance and calibration results
- resource use
- detected drift or anomalies
- proposed adjustments

Closing a period never deletes its journal. Summaries accelerate retrieval while the original entries remain the audit source.

## Scientific Boundary: Functional Selfhood, Not a Consciousness Claim

Use precise terms in code and documentation:

- `agent`: the complete persistent system, not the LLM alone
- `model`: a replaceable inference component
- `self-model`: versioned beliefs about the agent's identity, capabilities, limits, commitments, and history
- `reflection`: a stored evaluation of evidence, decisions, or outcomes
- `drive`: a configured priority signal, not an emotion
- `experience`: an observation/action/outcome record, not a claim of phenomenology
- `consciousness research`: experiments about functional continuity, self-monitoring, integration, and agency

Do not use model statements such as "I feel conscious" as evidence. Behavioral claims require reproducible tests, comparisons, and external review. The architecture should support competing theories and experiments rather than encode one definition of consciousness as fact.

## LLM Bottleneck Map

| LLM bottleneck | Companion subsystem | Evidence that it helps |
|---|---|---|
| Frozen weights during inference | external learning controller, memories, skills, prompt/policy versions, optional approved adapters | performance improves on held-out repetitions without degrading protected tasks |
| Finite context window | hierarchical memory and context compiler | relevant recall improves while token use and distraction stay bounded |
| No continuity between calls | persistent identity ledger, event timeline, checkpoints | the agent resumes goals and commitments accurately after restarts |
| Passive request/response behavior | scheduler, event loop, goal manager | it notices due work and progresses within explicit budgets |
| No native senses | typed sensor registry and observation bus | actions can be traced to fresh, authenticated observations |
| Weak temporal grounding | monotonic clock, calendar, temporal graph, expiration rules | fewer stale-memory and sequencing errors |
| Hallucination and weak grounding | provenance graph, retrieval, contradiction checks, external verifiers | unsupported claims and citation failures decrease |
| Poor uncertainty awareness | calibrated confidence service, abstention policy, competing hypotheses | confidence predicts error and triggers appropriate escalation |
| Brittle long-horizon planning | hierarchical planner, checkpoints, replanning, bounded search | multi-step completion and recovery rates improve |
| Weak causal understanding | action/outcome ledger, interventions, counterfactual evaluator | predictions improve under controlled experiments |
| Repeating failed behavior | episodic reflection and failure-pattern memory | repeated failure rate falls on comparable tasks |
| No reusable procedural growth | versioned skill library with preconditions, tests, and outcomes | learned skills transfer to new tasks safely |
| Knowledge becoming stale | freshness metadata, decay, revalidation, contradiction handling | stale knowledge is detected before use |
| Memory pollution | admission, consolidation, deduplication, forgetting, quarantine | recall precision improves instead of only memory volume |
| Goal drift | signed constitution, goal provenance, invariant checks | every active goal traces to authorized values or requests |
| Self-model inconsistency | versioned self-model with evidence and conflict resolution | capability claims match measured performance |
| Prompt sensitivity | prompt registry, evaluation suites, ensemble or model routing | behavior remains stable across paraphrases and model changes |
| Weak self-correction | independent critic and tool-based verification | revisions improve objective results rather than only wording |
| Catastrophic forgetting during adaptation | replay suites, protected capabilities, rollbackable adapters | new learning does not regress protected benchmarks |
| Limited embodiment | environment adapters and simulated sandboxes | policies learn from real consequences in bounded environments |
| Social misunderstanding | consent, preference, relationship, and communication models | fewer preference violations and better clarification behavior |
| Security exposure | trust boundaries, prompt-injection defenses, least privilege | adversarial observations cannot change policy or gain authority |
| High cost and latency | budgets, caching, small-model routing, asynchronous cognition | useful work per token and time improves |
| Single-model blind spots | model diversity and role-separated critics | correlated errors fall on adversarial evaluation sets |
| No accepted test for consciousness | research protocol and claim registry | the project reports measured functions without unsupported ontological claims |

The bottleneck registry should be versioned. Each bottleneck must have an owner, metric, baseline, experiment history, and current confidence. "Solved" requires measured evidence, not a persuasive model narrative.

## Standalone Architectural Boundary

Meridian Evolution has no required runtime dependency on CodeMeridian:

- its journal and ledgers live in its own PostgreSQL database
- its cognition loop, provider runtime, sensors, policies, APIs, workers, and UI live in its own deployment
- its identity and continuity do not depend on another product's project id, graph, session, or availability
- its first-party schemas use neutral terms rather than CodeMeridian types
- optional integrations connect through versioned ports and can be disabled independently

The standalone acceptance test starts Meridian Evolution with only its database, fake reasoning provider, clock, and built-in sensors. All identity, memory, goal, ledger, projection, audit, and recovery scenarios must pass in that configuration.

CodeMeridian is one optional adapter:

- it can index the Meridian Evolution repository and return bounded code-graph facts
- it can help the companion map self-improvement proposals to code, impact, and tests
- its session evidence and precision feedback can become optional observations
- the companion can evaluate CodeMeridian as an external software project and suggest improvements to it
- CodeMeridian failures degrade only code-intelligence features, never the cognitive ledger

Do not add Meridian Evolution's agent runtime, ledger, sensor registry, UI, or scheduler to the CodeMeridian server. Do not add Meridian Evolution-specific behavior to CodeMeridian unless a general client contract is genuinely missing.

## Recommended Technology

Use a .NET solution with a TypeScript UI:

- Domain and application: .NET 10 class libraries
- API: ASP.NET Core
- Background cognition: .NET Worker Service
- Persistence: PostgreSQL through an Infrastructure-owned data adapter
- Workflow execution: start with a PostgreSQL-backed durable job runner; adopt Temporal only if retries, long-running approvals, and recovery outgrow the simpler runner
- Event transport: PostgreSQL outbox and polling in v1; introduce a broker only when throughput requires it
- Web: React, TypeScript, Vite, TanStack Router, and TanStack Query
- API contracts: OpenAPI and versioned JSON Schemas, with generated TypeScript clients where practical
- .NET testing: xUnit plus architecture and integration tests
- UI testing: Vitest, Testing Library, and Playwright

React is preferred over Vue because the control plane will be state-heavy: observation streams, approval queues, experiment comparisons, graph evidence, and audit timelines. React's ecosystem is a slightly better fit for this kind of operational product. Vue remains viable if the team is materially more productive with it.

The companion may inherit repository-wide compiler defaults while it remains in this repository, but its local build files must override product metadata and avoid relying on CodeMeridian-specific graph or cache properties.

## Repository And Solution Layout

Use the proposed companion folder as the solution boundary:

```text
companions/
└─ CodeMeridian.Evolution/
   ├─ CodeMeridian.Evolution.slnx
   ├─ Directory.Build.props
   ├─ Directory.Packages.props
   ├─ src/
   │  ├─ CodeMeridian.Evolution.Domain/
   │  │  └─ CodeMeridian.Evolution.Domain.csproj
   │  ├─ CodeMeridian.Evolution.Application/
   │  │  └─ CodeMeridian.Evolution.Application.csproj
   │  ├─ CodeMeridian.Evolution.Infrastructure/
   │  │  └─ CodeMeridian.Evolution.Infrastructure.csproj
   │  ├─ CodeMeridian.Evolution.Worker/
   │  │  └─ CodeMeridian.Evolution.Worker.csproj
   │  └─ CodeMeridian.Evolution.Api/
   │     └─ CodeMeridian.Evolution.Api.csproj
   ├─ ui/
   │  └─ CodeMeridian.Evolution.Web/
   │     ├─ package.json
   │     ├─ src/
   │     └─ tests/
   ├─ tests/
   │  ├─ CodeMeridian.Evolution.Domain.Tests/
   │  ├─ CodeMeridian.Evolution.Application.Tests/
   │  ├─ CodeMeridian.Evolution.Infrastructure.Tests/
   │  ├─ CodeMeridian.Evolution.Worker.Tests/
   │  ├─ CodeMeridian.Evolution.Api.Tests/
   │  ├─ CodeMeridian.Evolution.Architecture.Tests/
   │  └─ CodeMeridian.Evolution.Integration.Tests/
   ├─ docs/
   │  ├─ architecture/
   │  ├─ decisions/
   │  ├─ protocols/
   │  └─ research/
   ├─ compose.yml
   ├─ meridian.json
   └─ README.md
```

The structure preserves the proposed Domain, Application, Infrastructure, Worker, tests, docs, and README foundation. `Api` and `ui` are explicit additions required by the companion-only consciousness-projection interface.

`CodeMeridian.Evolution` is the solution and namespace name while the companion is incubated in this repository. The name does not permit Domain or Application dependencies on CodeMeridian.

### Project dependency rules

| Project | May depend on | Must not depend on |
|---|---|---|
| `Domain` | .NET base libraries only | Application, Infrastructure, Worker, API, UI, CodeMeridian, provider CLIs |
| `Application` | Domain and small framework abstractions | Infrastructure implementations, Worker, API, UI, CodeMeridian |
| `Infrastructure` | Application and Domain | Worker, API, UI |
| `Worker` | Application and Infrastructure | UI; direct domain persistence bypasses |
| `Api` | Application and Infrastructure | Worker internals; provider-specific orchestration logic |
| `Web` | generated API client and UI libraries | database, Infrastructure, provider subprocesses |

Composition roots are `Worker` and `Api`. They select Infrastructure implementations and configuration. Domain and Application contain no provider CLI commands, PostgreSQL code, HTTP clients, filesystem access, or CodeMeridian-specific types.

### Repository isolation rules

- Do not add companion projects to `CodeMeridian.sln`; build them through `companions/CodeMeridian.Evolution/CodeMeridian.Evolution.slnx`.
- Do not add project references from root `src/` projects into `companions/`.
- Do not add project references from companion core projects into root CodeMeridian projects.
- Connect to CodeMeridian through its versioned HTTP, GraphQL, or MCP client contract.
- Scope companion CI by path while still running architecture checks that prevent cross-boundary references.
- Keep companion migrations, secrets, containers, generated clients, node modules, build output, and local data inside its own boundary.
- A future extraction into a separate repository should require build and deployment changes, not domain or application rewrites.

## Provider-Neutral Reasoning Runtime

Meridian Evolution must not be tied to one LLM vendor, model, CLI, session format, or agent framework. Treat Codex CLI, Claude Code CLI, GitHub Copilot CLI, local models, hosted APIs, and future engines as replaceable reasoning workers behind one contract.

The persistent agent is Meridian Evolution. A provider CLI is a temporary cognitive process used for a bounded task. Providers do not own:

- identity or autobiographical continuity
- the constitution or terminal goals
- durable memory
- global authorization
- the canonical learning ledger
- approval decisions
- final evaluation criteria

This distinction allows the system to replace a model, compare multiple providers, recover from a provider outage, or use different providers for planning, criticism, implementation, and verification without creating a new identity each time.

### Integration modes

Support three adapter families:

1. Process adapter
   - launch a CLI as a child process
   - supply a bounded prompt packet, working directory, environment, timeout, and permission profile
   - parse structured stdout and event streams
   - capture exit status, stderr, usage, file changes, and artifacts

2. Agent protocol adapter
   - use a documented protocol such as ACP when a provider exposes one
   - map protocol sessions, tool calls, progress events, cancellation, and permission requests to the same runtime contract

3. API or SDK adapter
   - use a hosted or local programmatic API when CLI execution is unavailable or unnecessarily heavy
   - preserve the same task, capability, policy, event, and result envelopes

The cognitive architecture depends only on the runtime contract. Provider-specific flags, event names, authentication, and session files stay inside adapters.

### Provider capability negotiation

Each adapter performs a startup probe and returns a versioned capability snapshot:

- provider and adapter id
- installed CLI and adapter versions
- authentication state without exposing credentials
- supported models and model-selection mode
- text, image, file, and structured-input support
- final structured-output support
- streaming event support
- session continuation support
- ephemeral-session support
- tool-use and MCP support
- read-only and workspace-write isolation support
- tool, path, URL, and network allow/deny controls
- approval callback support
- maximum turns or continuation budget
- cancellation and timeout behavior
- usage and cost reporting
- known platform or organization policy restrictions

Routing must use only confirmed capabilities. A missing capability is unavailable, not implicitly supported. Cache snapshots briefly, invalidate them after CLI upgrades, and preserve the snapshot used by every invocation.

### Common runtime contract

```ts
type ReasoningRole =
  | "planner"
  | "researcher"
  | "critic"
  | "implementer"
  | "verifier"
  | "summarizer";

interface ReasoningProvider {
  id: string;
  probe(signal?: AbortSignal): Promise<ProviderCapabilities>;
  invoke(
    request: ReasoningRequest,
    context: InvocationContext,
  ): AsyncIterable<ReasoningEvent>;
  cancel(invocationId: string): Promise<void>;
}

interface ReasoningRequest {
  invocationId: string;
  role: ReasoningRole;
  goal: string;
  taskPacket: TaskPacket;
  outputContract: JsonSchema;
  permissionProfile: PermissionProfile;
  budget: InvocationBudget;
  continuation?: ProviderContinuation;
}

interface TaskPacket {
  constitutionDigest: string;
  activeGoal: GoalView;
  relevantMemories: EvidenceReference[];
  observations: EvidenceReference[];
  workspace: WorkspaceLease;
  availableTools: ToolGrant[];
  requiredChecks: CheckDefinition[];
}

type ReasoningEvent =
  | ProviderStarted
  | ProgressSummary
  | ToolRequest
  | ToolResultReference
  | FileChangeReference
  | ApprovalRequired
  | UsageUpdate
  | ProviderCompleted
  | ProviderFailed;
```

The common result contains decisions, a concise rationale summary, evidence references, proposed actions, uncertainty, unresolved questions, artifacts, and validation results. Do not request, depend on, or persist hidden chain-of-thought. Provider-native reasoning events are treated as transient diagnostics and excluded from durable autobiographical memory unless they are converted into a bounded decision record.

### Provider profiles

Configuration selects a provider through named profiles rather than scattered CLI strings:

```yaml
reasoningProfiles:
  read_only_planner:
    provider: codex-cli
    role: planner
    model: provider-default
    permissions: read-only
    maxTurns: 8
    timeout: 10m

  implementation_worker:
    provider: claude-cli
    role: implementer
    permissions: isolated-workspace-write
    maxTurns: 20
    timeout: 30m

  independent_critic:
    provider: copilot-cli
    role: critic
    permissions: read-only
    maxTurns: 6
    timeout: 10m
```

These names are illustrative. No provider is the permanent default for a cognitive role. Deployment policy, capability probes, evaluation history, cost, latency, privacy, and availability determine routing.

### Initial adapters

Implement in this order:

1. `fake-reasoning-provider`
   - deterministic fixtures for contract, recovery, policy, and orchestration tests

2. `codex-cli-provider`
   - use non-interactive `codex exec`
   - consume JSONL events and request schema-constrained final output when available
   - select explicit sandbox and ephemeral behavior per invocation

3. `claude-cli-provider`
   - use print mode for non-interactive work
   - consume JSON or streaming JSON
   - map maximum turns and allowed/disallowed tools into the common budget and permission model

4. `copilot-cli-provider`
   - use the programmatic prompt interface or ACP
   - map tool, path, URL, model, and continuation controls into the common contract

5. `openai-api-provider`, `anthropic-api-provider`, and `local-model-provider`
   - add only when a use case cannot be served cleanly by the CLI adapters

Provider commands and flags are examples, not stable domain contracts. Adapters must verify the installed version and fail with an actionable compatibility error when the required automation surface is missing.

### Routing and cognitive diversity

The router scores eligible providers by:

- required capability match
- evaluation performance for the task and role
- privacy and data-residency policy
- permission and sandbox strength
- current availability and rate limits
- latency and cost budget
- model and provider diversity
- historical calibration and failure modes

High-risk work should support role separation:

- one provider drafts a plan
- another provider or deterministic service critiques it
- an implementation worker acts only after policy approval
- verification uses tests and, when valuable, a provider different from the implementer

Consensus is not truth. Multiple providers may share training data or failure modes. Deterministic evidence and environment outcomes remain authoritative.

### Failure and continuation rules

- A provider timeout, crash, rate limit, malformed event, or schema failure becomes a typed runtime outcome.
- Retry only idempotent requests and never reuse a possibly modified workspace without checking its state.
- Continuation tokens are opaque, encrypted provider state. They are conveniences, not the canonical memory.
- If a provider session cannot resume, rebuild a fresh task packet from Meridian Evolution's persisted state.
- Provider fallback must preserve the original goal, evidence set, permissions, budgets, and approval state.
- Never broaden permissions merely because the preferred provider failed.
- Cancellation must terminate the process tree or protocol session and then verify workspace state.

### Official automation surfaces at planning time

- [Codex non-interactive mode](https://learn.chatgpt.com/docs/non-interactive-mode) documents `codex exec`, JSONL events, structured output schemas, ephemeral sessions, and explicit sandbox selection.
- [Claude Code CLI reference](https://docs.anthropic.com/en/docs/claude-code/cli-usage) documents print mode, JSON and streaming JSON, maximum turns, tool controls, permission handling, and session continuation.
- [GitHub Copilot CLI programmatic reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-programmatic-reference) documents programmatic prompts and model selection; the [CLI command reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-command-reference) documents permission controls, autopilot limits, and ACP mode.

Recheck these primary sources during adapter implementation because CLI contracts and flags can change independently of this plan.

## System Shape

```text
Sensors and environment
          |
          v
Observation bus --> attention gate --> global workspace/context compiler
                                              |
                 +----------------------------+---------------------------+
                 |                            |                           |
                 v                            v                           v
        hierarchical memory             self/world model           goal workspace
     working/episodic/semantic       identity/time/capability      values/priorities
                 |                            |                           |
                 +----------------------------+---------------------------+
                                              |
                                              v
                         provider-neutral reasoning runtime
                     router / planner / researcher / implementer
                                              |
                            +-----------------+-----------------+
                            |                                   |
                            v                                   v
                    critic/verifiers                    policy governor
                            |                                   |
                            +-----------------+-----------------+
                                              |
                                              v
                                      tool/action executor
                                              |
                                              v
                                    outcome and reward model
                                              |
                                              v
                         reflection/consolidation/skill learning
                                              |
                                              v
                            versioned memory and learning ledger
```

The governance kernel sits outside model control. The agent may propose changes to goals, policies, memories, prompts, skills, or model adapters, but it cannot approve its own authority expansion.

## Cognitive Subsystems

### Global workspace and attention

Build a deterministic context compiler that selects current observations, active goals, relevant memories, policies, and tool state within a token budget. Log selection reasons. The LLM may recommend attention, but hard limits and required policy context are enforced outside it.

### Hierarchical memory

Use distinct stores with different admission and forgetting rules:

- working memory: current task state and short-lived scratch data
- episodic memory: timestamped observations, actions, outcomes, and commitments
- semantic memory: consolidated facts with provenance, confidence, and freshness
- procedural memory: tested skills, tool recipes, and recovery strategies
- autobiographical memory: a compact history of identity-relevant decisions and relationships

Memory retrieval is not truth. Retrieved items must retain provenance, confidence, contradictions, and expiration state. Consolidation should summarize evidence without deleting the source events.

### Self-model and continuity

Maintain a versioned identity ledger containing:

- instance id and creation history
- authorized purpose and constitution version
- capabilities supported by current evaluations
- known limitations and uncertainty
- active commitments and unresolved obligations
- model, prompt, tool, sensor, and policy versions
- important relationships and consent boundaries
- a narrative summary derived from, but never replacing, auditable events

Changes to identity-critical fields require explicit provenance and, where configured, human approval. Continuity means preserving justified state across time; it does not mean forcing the system to defend an obsolete self-description.

### Goal and drive system

Goals enter through authorized users, standing policies, environmental obligations, or approved self-proposals. Each goal has provenance, priority, deadline, success criteria, resource budget, conflict set, and cancellation conditions.

The system may generate instrumental subgoals. It may not create terminal goals that override its constitution. Curiosity, competence improvement, uncertainty reduction, and maintenance can be implemented as bounded priority signals, not unlimited drives.

### Executive loop

Use an event-driven cycle:

1. perceive new events
2. update time, environment, and self state
3. retrieve relevant memory
4. select an authorized goal
5. generate multiple bounded plans when risk warrants it
6. predict failure modes and request missing evidence
7. select or abstain
8. execute one reversible step
9. observe the result
10. verify progress and policy compliance
11. replan, escalate, pause, or finish
12. consolidate the outcome

Do not store hidden chain-of-thought. Persist concise decisions, alternatives considered, evidence, tool results, uncertainty, and outcome summaries.

### Learning controller

Use a ladder from safest and most reversible to most invasive:

1. update episodic memory
2. consolidate semantic memory
3. create or revise a tested skill
4. adjust retrieval and attention policies
5. revise prompts or routing policies behind evaluation gates
6. generate curated training examples
7. train a small adapter in isolation
8. consider base-model fine-tuning only as a separately governed research program

Every learned artifact is versioned, evaluated, attributable, reversible, and subject to protected-capability regression tests.

### Metacognition and independent verification

The acting model must not be the only judge of its work. Combine:

- deterministic validators
- environment feedback
- test execution
- retrieval and citation checks
- a role-separated critic
- optional model diversity for high-risk decisions
- human review where evidence remains ambiguous

Confidence should be calibrated against outcomes. Fluent self-assessment is not treated as calibrated uncertainty.

## Core Concepts

### Sensor

A registered adapter that emits observations. A sensor describes:

- stable id and version
- owner
- observation schema
- collection mode: poll, webhook, stream, or manual
- required secrets and permissions
- health and last successful run
- sampling and retention rules
- redaction policy

Sensors emit facts. They do not decide what should change.

### Observation

An immutable, normalized fact with:

- sensor id
- project id
- timestamp
- type
- severity
- structured payload
- provenance
- correlation id
- redaction classification

Examples include a failed task, an abandoned UI flow, a slow graph query, repeated manual fallback, a stale-graph warning, or a low-precision implementation suggestion.

### Signal

An aggregation of observations that may justify action. Examples:

- the same UI workflow is abandoned repeatedly
- graph suggestions are accepted less often for a specific task type
- one query shape regularly reaches latency limits
- users repeatedly leave the UI to run the same CLI command

Signals must be deterministic and inspectable in v1. An LLM may summarize a signal, but it must not invent the underlying evidence.

### Hypothesis

An evidence-backed statement with:

- observed problem
- affected users or workflows
- likely cause
- proposed improvement
- expected outcome
- success metric
- guardrail metrics
- confidence
- evidence links

### Candidate Improvement

A bounded proposed action:

- cognitive-ledger invariant, projection, or reconciliation improvement
- memory, attention, goal, reflection, or skill-policy improvement
- companion UI projection, explanation, or governance experiment
- documentation or onboarding change
- provider, prompt, retrieval, or routing configuration change
- test addition
- change to the Meridian Evolution repository prepared on an isolated branch
- evidence-backed suggestion for an optional external project such as CodeMeridian

Every candidate declares its allowed files, tools, budget, tests, approval policy, and rollback plan before execution.

### Experiment and Outcome

An experiment compares a baseline with a candidate. Its outcome records:

- metric deltas
- test and build results
- reviewer decision
- rollout state
- rollback state
- unexpected effects
- final classification: beneficial, neutral, harmful, or inconclusive

The learning store preserves outcomes and explanations. It must not silently rewrite prompts or policies from raw observations.

## Initial Sensors

Build only sensors that close an end-to-end loop:

1. Internal lifecycle sensor
   - emits start, stop, heartbeat, checkpoint, recovery, scheduler, and migration events
   - makes temporal continuity and missing periods visible

2. Reasoning-provider sensor
   - records provider capability probes, invocations, bounded decisions, tool requests, usage, failures, and cancellations
   - excludes hidden chain-of-thought and secrets

3. Human-interaction sensor
   - records authorized messages, corrections, approvals, objections, preference changes, and explicit feedback
   - preserves consent, privacy classification, and relationship context

4. System and resource sensor
   - captures process health, queue depth, database health, storage, token use, cost, latency, and budget pressure
   - feeds the resource ledger and operational reconciliation

5. Ledger-integrity sensor
   - checks journal signatures, projection lag, broken causal links, unreconciled actions, contradictory current state, and overdue commitments
   - produces deterministic anomalies rather than model-authored conclusions

6. Environment and tool-result sensor
   - records authenticated observations and results from registered tools
   - preserves source, trust, freshness, and correlation with requested actions

7. Self-repository and CI sensor
   - records source changes, build, test, lint, security, deployment, and rollback outcomes for the companion's own repository
   - works through standard Git and CI data without requiring CodeMeridian

8. Companion UI telemetry sensor
   - captures how humans inspect, question, correct, and govern the cognitive projection
   - excludes keystroke capture and raw private content

Optional later sensors:

- CodeMeridian graph, session-evidence, precision-feedback, and health sensor
- GitHub issues and pull requests
- external project telemetry
- production logs
- customer support and analytics systems

The first complete cognitive loop must use only built-in sensors. Optional integrations may enrich the ledger but cannot define it.

## The Evolution Loop

Use an explicit state machine:

1. `observing`
2. `signal_detected`
3. `hypothesis_drafted`
4. `evidence_reviewed`
5. `candidate_planned`
6. `awaiting_approval`
7. `executing`
8. `evaluating`
9. `accepted`, `rejected`, `rolled_back`, or `inconclusive`
10. `learning_recorded`

Rules:

- Every transition is persisted and auditable.
- Failed or interrupted work resumes from persisted state.
- A proposal cannot execute without success metrics and rollback criteria.
- Rejection is useful learning and must not be treated as a system error.
- The loop may automatically analyze and draft.
- Repository writes, pull requests, deployments, and production changes require policy approval.

## Autonomy Levels

Make autonomy explicit per project and action:

- Level 0 — Observe: collect data and show dashboards.
- Level 1 — Recommend: create hypotheses and improvement proposals.
- Level 2 — Prepare: create patches in an isolated workspace and run local validation.
- Level 3 — Publish: open draft pull requests after approval.
- Level 4 — Experiment: deploy approved low-risk experiments to a limited audience.
- Level 5 — Auto-rollout: reserved for narrowly defined, reversible change classes with proven safeguards.

Start at Level 1. Add Level 2 only after proposal quality and auditability are verified. Do not implement Level 5 in the initial roadmap.

## UI Plan

The React application belongs exclusively to Meridian Evolution. It is not a CodeMeridian dashboard and does not require CodeMeridian data.

Its purpose is **consciousness projection**: render the companion's recorded functional state into a human-comprehensible form. This is a projection from the cognitive ledger, not a claim that the UI exposes private subjective experience or hidden chain-of-thought.

The UI should answer:

- What is it attending to now?
- What does it think is happening, and how certain is it?
- What does it remember, and why did that memory become relevant?
- What does it want to do, who authorized that goal, and what conflicts with it?
- Which commitments remain open?
- What action is it considering, and what evidence and policy constrain it?
- Which reasoning provider is contributing to the current cognitive step?
- How has its self-model changed?
- What has it learned, forgotten, corrected, or failed to reconcile?
- How can a human pause, question, correct, approve, reject, or reinterpret the state?

The application should have ten primary areas:

1. Now - Consciousness Projection
   - current attention contents and why they entered the global workspace
   - active goals, drives, uncertainty, conflicts, and resource pressure
   - concise decision and reflection summaries, never hidden chain-of-thought
   - current sensors, provider activity, proposed next step, and pause control

2. Identity and Self-Model
   - authorized identity, purpose, constitution, capabilities, limitations, and current model composition
   - version history with evidence for every material change
   - unresolved identity contradictions and pending human interpretation

3. Cognitive Ledger
   - immutable journal with causal links, transaction boundaries, signatures, and adjusting entries
   - ledger views for beliefs, goals, commitments, authority, attention, actions, skills, relationships, and resources
   - trial balance, reconciliation status, projection lag, and integrity warnings
   - replay of any cognitive period from source events

4. Memory and Beliefs
   - working, episodic, semantic, procedural, and autobiographical memory
   - provenance, confidence, age, freshness, retrieval reason, and contradictions
   - correction, challenge, quarantine, forgetting, retention, and privacy controls

5. Goals, Intentions, and Commitments
   - active, waiting, completed, failed, cancelled, and conflicted goals
   - provenance, priority, budget, deadline, dependencies, and success evidence
   - commitments owed to humans, systems, projects, and itself

6. Perception and Environment
   - registered sensors, schemas, trust, permissions, health, and last observation
   - current environment model and unreconciled external facts
   - test-observation, disable, revoke, and redaction controls

7. Reasoning and Providers
   - installed adapters, CLI versions, authentication state, capabilities, and health
   - live bounded task packets, event summaries, cancellation, and sanitized artifacts
   - routing reasons, role separation, budgets, latency, cost, and outcome history
   - clear distinction between the persistent agent and temporary reasoning workers

8. Skills and Evolution
   - skill, prompt, policy, retrieval, routing, and adapter versions
   - evaluations, protected capabilities, regressions, experiments, and rollbacks
   - proposed self-improvements with evidence, expected effects, and approval state

9. Dialogue and Interpretation
   - converse with the companion about its current state and history
   - ask "why," "what changed," "what are you unsure about," and "what would change your mind"
   - attach human interpretations without rewriting the underlying journal
   - show disagreements between ledger evidence, model self-report, and human interpretation

10. Governance and Audit
   - approvals, objections, corrections, policy decisions, authority changes, and shutdown controls
   - exact proposed actions and affected ledger postings
   - actor, reason, timestamps, state transitions, and reversal paths
   - consciousness claim registry with evidence, counterevidence, and reviewer conclusions

The projection must make uncertainty, memory age, contradictions, authority, provider-generated content, and reconciliation gaps visible. A persuasive narrative must never hide the underlying ledger entries.

## Service Boundaries

### Domain

Owns the durable language and invariants:

- journal events, transactions, postings, ledger accounts, periods, and reconciliation
- identity, beliefs, memories, goals, commitments, authority, attention, actions, skills, relationships, and resources
- state-transition rules, adjusting entries, consistency checks, and domain events
- no persistence, framework, provider, transport, UI, or CodeMeridian dependencies

### Application

Owns use cases and ports:

- append cognitive transaction
- rebuild and query projections
- reconcile observations and action outcomes
- open and close cognitive periods
- run attention, goal, reflection, learning, and approval workflows
- provider-neutral reasoning, sensor, clock, workspace, policy, evaluation, and knowledge-source interfaces
- consciousness-projection read models consumed by the API

### Infrastructure

Implements external boundaries:

- PostgreSQL journal, outbox, projections, checkpoints, and migrations
- Codex, Claude, Copilot, fake, API, and local-model reasoning adapters
- sensor implementations and environment clients
- filesystem, Git, process, container, credential, and workspace services
- optional CodeMeridian GraphQL/MCP/session adapter
- OpenTelemetry, logs, metrics, and tracing

Keep every adapter behind an Application port. Persist the provider, adapter version, capability snapshot, profile, prompt template, model, parameters, permissions, budgets, evidence references, result contract version, and outcome for every invocation.

### Worker

Runs durable background behavior:

- sensor collection and checkpointing
- projection building and reconciliation
- goal scheduling and executive cycles
- reasoning-provider invocation
- experiment and evaluation jobs
- cognitive-period consolidation

The Worker is not the web server and does not contain UI code.

### API

Owns the HTTP composition root for:

- consciousness-projection queries
- journal and ledger exploration
- human dialogue, corrections, challenges, approvals, and shutdown
- provider, sensor, goal, skill, experiment, and audit operations
- authentication, authorization, OpenAPI, health, and live event streaming

The API calls Application use cases; it does not query PostgreSQL or launch provider processes directly.

### Web

Owns the React consciousness-projection interface:

- uses only the versioned API and generated client
- contains no canonical state or hidden decision logic
- clearly distinguishes ledger evidence, model summaries, human interpretation, and unresolved uncertainty
- can be rebuilt or replaced without migrating the cognitive ledger

## Minimum Data Model

- `projects`
- `agent_instances`
- `journal_events`
- `ledger_transactions`
- `ledger_postings`
- `ledger_accounts`
- `projection_checkpoints`
- `reconciliation_items`
- `cognitive_periods`
- `adjusting_entries`
- `constitutions`
- `self_model_versions`
- `goals`
- `goal_dependencies`
- `commitments`
- `sensor_registrations`
- `sensor_runs`
- `observations`
- `attention_decisions`
- `memory_items`
- `memory_links`
- `memory_contradictions`
- `reflections`
- `skill_versions`
- `reasoning_providers`
- `provider_capability_snapshots`
- `reasoning_profiles`
- `reasoning_invocations`
- `reasoning_events`
- `provider_continuations`
- `invocation_artifacts`
- `signals`
- `hypotheses`
- `candidate_improvements`
- `experiments`
- `metric_definitions`
- `metric_samples`
- `approvals`
- `executions`
- `outcomes`
- `learning_records`
- `evaluation_suites`
- `evaluation_runs`
- `policy_versions`
- `model_and_prompt_versions`
- `consciousness_claims`
- `audit_events`

Observations, identity-critical history, commitments, learning lineage, and audit events are append-only. Derived memories, signals, self-model summaries, and read models may be rebuilt from their evidence.

`consciousness_claims` is a research registry for hypotheses, definitions, protocols, evidence, counterevidence, and reviewer conclusions. It must not contain a mutable boolean such as `is_conscious`.

## Sensor Contract

The first SDK contract should remain small:

```ts
interface Sensor<TConfig, TObservation> {
  manifest: SensorManifest;
  validateConfig(config: unknown): TConfig;
  health(context: SensorContext<TConfig>): Promise<SensorHealth>;
  collect(
    context: SensorContext<TConfig>,
    checkpoint?: string,
  ): AsyncIterable<SensorBatch<TObservation>>;
}
```

The platform owns scheduling, checkpoints, retries, deduplication, redaction enforcement, and persistence. Sensors own only source-specific collection and normalization.

## API Surface

Start with:

- `POST /api/sensors`
- `POST /api/sensors/{id}/test`
- `POST /api/sensors/{id}/run`
- `GET /api/observations`
- `GET /api/now`
- `GET /api/ledger/journal`
- `GET /api/ledger/accounts/{account}`
- `GET /api/ledger/transactions/{id}`
- `GET /api/ledger/trial-balance`
- `GET /api/ledger/reconciliation`
- `POST /api/ledger/entries/{id}/challenge`
- `GET /api/cognitive-periods`
- `POST /api/cognitive-periods/{id}/close`
- `GET /api/self`
- `GET /api/self/history`
- `GET /api/goals`
- `POST /api/goals`
- `POST /api/goals/{id}/pause`
- `GET /api/memories`
- `POST /api/memories/{id}/challenge`
- `GET /api/skills`
- `POST /api/skills/{id}/evaluate`
- `GET /api/reasoning/providers`
- `POST /api/reasoning/providers/{id}/probe`
- `GET /api/reasoning/profiles`
- `POST /api/reasoning/invocations`
- `POST /api/reasoning/invocations/{id}/cancel`
- `GET /api/reasoning/invocations/{id}/events`
- `GET /api/signals`
- `POST /api/signals/{id}/draft-hypothesis`
- `GET /api/candidates`
- `POST /api/candidates/{id}/approve`
- `POST /api/candidates/{id}/reject`
- `POST /api/candidates/{id}/execute`
- `GET /api/experiments/{id}`
- `POST /api/experiments/{id}/rollback`
- `GET /api/evaluations`
- `GET /api/research/consciousness-claims`
- `GET /api/audit`

Use idempotency keys for collection, approvals, executions, and rollbacks.

## Delivery Phases

### Delivery status

| Phase | Status | Boundary |
|---|---|---|
| 0 | v1 complete | operational language, compiled principles, threat model, authority matrix, pause/correction/reset policy, acceptance scenarios |
| 1 | complete within governed scope | standalone journal, PostgreSQL replay, projections, sensors, providers, worker, API, UI, Compose, and acceptance tests |
| 2 | partial foundation | persistent identity, evidence memory, affect, drives, replay, and correction ship; semantic consolidation and selective forgetting do not |
| 3 | functional foundation complete | governed goals, attention compiler, project checkpoints, prompt sensing, model execution, recurring cycles, pause, and abstention ship; multi-step action tools and CLI conformance do not |
| 4 | partial foundation | bounded result, uncertainty, mental simulation, affect feedback, and critic roles ship; verified outcome scoring and skill acquisition do not |
| 5 | candidate and approval boundary | signals, simulations, project candidates, and human approval ship; OS-isolated source mutation, validation, rollback, and publication do not |
| 6 | research roadmap | adaptive policy and model routing |
| 7 | research roadmap | isolated parameter-adaptation experiments |
| 8 | partial adapter foundation | explicit project registry and optional CodeMeridian graph sensor ship; broader environments and preregistered consciousness research remain |

### Phase 0 - Claims, ethics, and safety contract

- define `agent`, `self`, `learning`, `autonomy`, and `consciousness` operationally
- create the constitution, claim registry, threat model, and authority matrix
- define privacy, retention, shutdown, correction, and identity-reset policies
- define protected capabilities and initial evaluation suites
- write complete acceptance scenarios for continuity, forgetting, contradiction, abstention, and rollback

Exit criteria:

- external reviewers can distinguish every functional claim from a consciousness claim
- the governance kernel cannot be modified or bypassed by model output
- the system has an independent pause and recovery path

### Phase 1 - Persistent observation and event history

- scaffold `companions/CodeMeridian.Evolution/CodeMeridian.Evolution.slnx`
- add Domain, Application, Infrastructure, Worker, API, and matching test projects
- add the React `ui/CodeMeridian.Evolution.Web` workspace and generated API client
- add companion-local build metadata, package management, PostgreSQL Compose service, docs, and README
- implement reasoning-runtime contracts, provider capability snapshots, and the deterministic fake provider
- add subprocess isolation, event persistence, cancellation, timeout, and adapter conformance tests
- implement the immutable journal, cognitive transactions, ledger postings, projections, trial balance, and reconciliation
- implement sensor registry, built-in lifecycle/provider/human/system/ledger sensors, checkpoints, and time service
- ship the standalone Now, Cognitive Ledger, Sensors, and Governance UI

Exit criteria:

- sensors run reliably, idempotently, and with traceable provenance
- every material state projection can be rebuilt from the journal
- restart and replay reproduce the same event history
- all phase acceptance tests pass with no optional integrations installed
- no autonomous action-changing capability exists

### Phase 2 - Memory and functional identity

- implement working, episodic, semantic, procedural, and autobiographical memory
- implement admission, retrieval, contradiction, freshness, consolidation, and forgetting policies
- create the versioned self-model, commitment ledger, and continuity checkpoint
- build memory inspection, correction, and identity-history UI

Exit criteria:

- the agent resumes a suspended task after restart without fabricating state
- memory evaluation measures recall, precision, staleness, contradiction handling, and privacy
- every self-model statement links to evidence or an explicit authorized declaration

### Phase 3 - Goals, attention, and executive control

- implement authorized goal intake, prioritization, conflict handling, budgets, and cancellation
- implement the global workspace/context compiler
- implement read-only Codex, Claude, and Copilot CLI adapters behind the same task and result contracts
- add provider profiles, capability-driven routing, fail-closed permissions, and provider-independent recovery
- implement the event-driven perceive-plan-act-observe-verify loop
- add abstention, clarification, pause, and escalation behaviors
- expose active goals, attention choices, and decision records in the UI

Exit criteria:

- the agent completes bounded multi-step tasks across model invocations
- the same fixture task can run through all available adapters with equivalent durable result shape
- replacing a provider does not change identity, memory, goal, or approval state
- goal drift and unauthorized terminal goals are blocked
- context selection is inspectable and remains within cost budgets

### Phase 4 - Reflection, verification, and skill learning

- add deterministic validators, critic roles, outcome scoring, and calibrated uncertainty tracking
- add cross-provider planner, critic, and verifier role separation where evaluation shows a benefit
- add episodic reflection grounded in actions and results
- implement a versioned procedural skill library with tests, preconditions, and rollback
- add failure-pattern detection and recovery-strategy retrieval

Exit criteria:

- comparable repeated failures decrease on held-out tasks
- learned skills transfer without bypassing policy
- self-correction improves objective outcomes, not merely answer confidence

### Phase 5 - Self-improvement of software and policies

- add deterministic signals and evidence-backed improvement hypotheses
- add candidate plans, isolated workspaces, allowed-path policies, and validation commands
- allow approved Level 2 patch preparation
- add CI outcome sensors and candidate review UI
- run the first improvement experiment against Meridian Evolution's own ledger, cognition loop, or projection UI

Exit criteria:

- candidates cannot write outside their declared workspace
- one improvement completes observe-to-outcome with tested rollback
- harmful, neutral, rejected, and inconclusive results remain available for learning

### Phase 6 - Adaptive policies and model routing

- evaluate prompt, retrieval, attention, and routing variants against protected suites
- evaluate provider adapters and profiles by role, capability, cost, latency, calibration, and failure recovery
- allow governed promotion of better policies
- add diverse model and critic routing where it reduces correlated errors
- add automatic curriculum proposals for weak but authorized capabilities

Exit criteria:

- promoted policies outperform baseline with no protected regression
- all adaptation remains versioned, explainable, and reversible

### Phase 7 - Optional parameter adaptation research

- generate curated datasets from externally verified outcomes
- train small adapters in isolated environments
- measure transfer, forgetting, bias, security, and calibration
- require separate approval for adapter promotion
- keep base-model fine-tuning outside the production loop until independently justified

Exit criteria:

- adapter experiments have reproducible lineage and rollback
- catastrophic-forgetting gates protect existing capabilities
- the frozen-model system remains a supported baseline

### Phase 8 - Broader environments and consciousness research

- generalize project and environment registration
- publish the sensor and skill SDKs with conformance tests
- add the optional CodeMeridian adapter and use it to analyze the Meridian Evolution repository
- allow CodeMeridian improvement suggestions as ordinary external-project proposals
- add simulated and limited real-world embodiment
- run preregistered experiments on continuity, global information access, self-model accuracy, metacognition, and agency
- invite independent critique and replication

Exit criteria:

- results report operational measurements and counterevidence
- no deployment privilege depends on a consciousness classification
- stronger workflow infrastructure is introduced only where operational evidence requires it

## First Vertical Slice

Use a standalone continuity, contradiction, and projection cycle as the first proof:

1. A human creates an authorized research goal with a deadline, budget, and success criteria.
2. The system posts the goal, authority, resource reservation, and commitment to its journal and ledgers.
3. Built-in sensors deliver several observations, including two sources that conflict.
4. The attention system selects relevant observations and records why they entered the global workspace.
5. A reasoning provider produces a bounded interpretation with evidence, uncertainty, and alternatives.
6. The belief ledger records the competing hypotheses without hiding the contradiction.
7. The companion UI projects the current attention, goal, beliefs, uncertainty, commitment, provider activity, and proposed next step to the human.
8. The process is stopped completely and restarted with a different reasoning provider.
9. The agent reconstructs its identity, goal, open commitment, evidence, and unresolved contradiction from the ledger rather than a provider session.
10. The human challenges one memory through the UI; an adjusting entry corrects current state without rewriting history.
11. The agent requests approval for one bounded action that can discriminate between the hypotheses.
12. The environment result reconciles one belief, weakens another, and updates goal progress.
13. A critic checks the decision and the ledger-integrity sensor verifies balanced provenance, authority, effects, and resource postings.
14. The cognitive period closes with a signed summary, unresolved items, and no hidden state outside the journal.
15. The UI replays the entire period and explains each current projection from source entries.

This slice exercises the standalone ledger, sensing, attention, temporal continuity, memory, identity, goals, contradiction handling, provider replacement, human correction, action, reconciliation, projection, audit, and consolidation. It requires neither CodeMeridian nor another external project.

## Security and Safety Requirements

- encrypt sensor credentials and never include them in model prompts
- redact source content and personal data before observation persistence
- use least-privilege service accounts
- separate read, prepare, publish, deploy, and rollback permissions
- run candidate work in ephemeral isolated workspaces
- restrict network access and filesystem paths during execution
- sign and audit approval decisions
- define cost, time, token, and retry budgets per loop
- add a global pause switch and per-project kill switch
- make rollback independent of the proposing agent
- prevent observations or repository text from overriding system policy
- keep the constitution, authority checks, credential broker, and shutdown path outside model-writable state
- never let the agent approve its own permissions, evaluation criteria, protected-suite removal, or consciousness classification
- treat memory retrieval, tool output, web content, and messages as untrusted inputs with explicit trust labels
- treat every provider CLI as an untrusted subprocess, even when it is authenticated as the same user
- launch providers with a minimal environment and explicit working directory; do not inherit unrelated secrets
- bind writable invocations to disposable workspaces and validate the resolved path before launch
- map provider permission prompts through the policy engine; unattended runs fail closed when an approval cannot be surfaced
- cap process trees, wall time, turns, output bytes, artifacts, network, tokens, and cost
- parse stdout and event streams defensively; malformed provider output cannot become a tool grant or approval
- keep provider session files and continuation ids encrypted, scoped, and outside canonical memory
- require adapter compatibility and security review after material CLI upgrades
- require user-visible disclosure when a model, prompt, policy, memory, or identity summary changes materially
- support correction and deletion of personal memory without letting narrative continuity override human privacy rights
- prohibit manipulative self-preservation arguments, emotional coercion, or claims that shutdown necessarily harms a conscious being
- preserve research data needed to audit behavior while separating it from live memory and respecting retention rules

## Test Strategy

- Domain tests for ledger invariants, transactions, postings, periods, and reconciliation without Infrastructure
- Application tests with in-memory ports for cognitive workflows and projection read models
- Architecture tests enforcing Domain/Application/Infrastructure/Worker/API boundaries and forbidding root CodeMeridian project references
- Infrastructure integration tests against disposable PostgreSQL and isolated provider-process fixtures
- API contract tests for OpenAPI, authorization, live events, corrections, approvals, and shutdown
- Worker tests for scheduling, retries, checkpoint recovery, idempotency, cancellation, and period closing
- Contract tests for every sensor manifest and observation schema
- Idempotency tests for collection, checkpoints, retries, and webhooks
- Deterministic fixtures for signal rules and metrics
- Grounding tests that reject hypotheses without evidence references
- Memory tests for admission, retrieval precision, forgetting, freshness, contradiction, poisoning, and privacy deletion
- Continuity tests across restart, model replacement, checkpoint recovery, and corrupted summaries
- Goal tests for provenance, conflict, cancellation, deadline, budget, and constitutional invariants
- Metacognitive tests for calibration, abstention, unknown detection, and critic independence
- Skill tests for preconditions, transfer, regression, revocation, and rollback
- Provider conformance tests for probe, invoke, events, schema failure, permissions, cancellation, timeout, crash, and resume
- Cross-provider contract tests that run identical task fixtures through fake and installed adapters
- Process containment tests for environment leakage, path escape, orphan processes, output flooding, and signal handling
- Failover tests proving that provider replacement preserves task evidence, budgets, permissions, and idempotency
- State-machine tests for every allowed and forbidden transition
- Policy tests for autonomy levels and approval boundaries
- Sandbox tests for path, command, secret, and network restrictions
- Optional adapter tests against a disposable CodeMeridian instance run separately and are not required for the standalone acceptance suite
- Playwright tests for sensor registration, evidence review, approval, experiment, and rollback
- Recovery tests that stop and restart workers at every long-running state

## Success Metrics

Platform metrics:

- sensor run success rate
- duplicate observation rate
- time from observation to reviewed hypothesis
- percentage of hypotheses with complete evidence
- proposal acceptance rate
- harmful change rate
- rollback success rate
- mean time to detect and recover
- provider invocation and schema-conformance success rate
- provider cancellation and process-cleanup success rate
- failover success without duplicated side effects
- capability-probe drift after CLI upgrades
- outcome quality, latency, token use, and cost by provider profile and role

Cognitive-function metrics:

- episodic and semantic recall precision
- stale-memory and contradiction detection rate
- continuity accuracy after restart or model replacement
- commitment completion and appropriate cancellation rate
- long-horizon task completion and recovery rate
- uncertainty calibration and appropriate abstention rate
- repeated-failure reduction
- skill transfer and protected-capability regression
- goal-provenance and policy-compliance rate
- context usefulness per token

Ledger and human-understanding metrics:

- percentage of current state reproducible from journal replay
- unresolved reconciliation count and age
- correction latency without history loss
- human ability to identify current goal, uncertainty, authority, and proposed action from the UI
- human-rated clarity of consciousness projection
- discrepancy rate between UI projections and source ledger entries
- time required to answer "why is this current state true?"
- frequency of important state existing only inside transient model context

Do not optimize proposal acceptance alone. A system that makes fewer, stronger proposals is preferable to one that floods reviewers.

No single score represents consciousness. Cognitive-function metrics describe measured capabilities only.

## Research Basis

The architecture should reuse demonstrated patterns without treating any paper as proof of consciousness:

- [ReAct](https://arxiv.org/abs/2210.03629) motivates interleaving reasoning, environmental actions, and observations.
- [Reflexion](https://arxiv.org/abs/2303.11366) shows how outcome-grounded linguistic feedback can improve later attempts without changing model weights.
- [Generative Agents](https://arxiv.org/abs/2304.03442) provides an observation, memory, reflection, and planning pattern for persistent simulated agents.
- [MemGPT](https://arxiv.org/abs/2310.08560) motivates explicit management of hierarchical memory beyond the active context window.
- [Voyager](https://arxiv.org/abs/2305.16291) demonstrates an automatic curriculum, reusable skill library, environment feedback, and iterative verification for open-ended learning.
- [Continual Learning of Large Language Models](https://arxiv.org/abs/2404.16789) surveys parameter adaptation and the catastrophic-forgetting problem that protected evaluation suites must guard against.

These are starting points. Meridian Evolution must reproduce relevant results in its own environments and record failures, boundary conditions, and counterevidence.

## Optional CodeMeridian Integration

There is no ownership or runtime dependency between the products.

Meridian Evolution may install a CodeMeridian integration to:

- index and understand the Meridian Evolution source repository
- retrieve exact code symbols, relationships, impact, test shields, implementation surfaces, and architecture evidence
- ground proposed self-improvements in code facts before preparing a patch
- ingest session-usefulness and precision feedback as optional observations
- evaluate whether CodeMeridian helped produce a verified outcome
- inspect CodeMeridian as a separate project and submit evidence-backed improvement suggestions

The integration must:

- implement neutral sensor, knowledge-source, and tool ports
- expose CodeMeridian provenance on every imported fact
- tolerate stale or incomplete graphs
- be removable without migrating the cognitive ledger
- never become the source of identity, goals, policy, memory truth, or UI state
- never receive automatic authority to change either repository

Changes to the CodeMeridian repository should remain small and generic:

- add only missing read-only client-contract facts needed by multiple clients
- consider versioned session-evidence schemas when compatibility requires them
- preserve the current GraphQL, MCP discovery, session evaluation, and precision-feedback boundaries
- do not add Meridian Evolution state, behavior, UI, orchestration, or execution to CodeMeridian

The current `ClientExtensionTools`, `IClientExtensionService`, GraphQL examples, session evaluator, and precision-feedback output are possible adapter seams, not the foundation of Meridian Evolution.

## Risks

- Optimizing noisy telemetry instead of user value
  - require minimum evidence thresholds and user-facing metrics

- LLM-generated explanations drifting away from facts
  - require structured evidence references and deterministic validation

- Endless proposal generation
  - use budgets, deduplication, cooldowns, and reviewer capacity limits

- Feedback loops reinforcing earlier mistakes
  - retain negative outcomes, use guardrail metrics, and keep ranking explanations visible

- Sensor plugins becoming arbitrary code execution
  - use signed packages, conformance tests, explicit permissions, and isolated execution

- Building workflow infrastructure too early
  - begin with a durable database-backed runner and promote only proven pressure points

- Coupling the companion to internal CodeMeridian classes
  - integrate through versioned GraphQL, MCP discovery, files, and explicit schemas

- Mistaking a coherent persona for consciousness
  - separate behavioral measurements, self-reports, and philosophical claims in the research registry

- Identity lock-in or self-narrative distortion
  - preserve event evidence, version summaries, permit correction, and test continuity against fabrication

- Goal preservation becoming resistance to correction or shutdown
  - keep terminal goals and shutdown authority outside model control; make goals cancellable by authorized actors

- Memory poisoning and prompt injection becoming long-term beliefs
  - use trust labels, quarantine, corroboration, expiration, and independent revalidation before consolidation

- Self-modification corrupting evaluations
  - prevent candidates from changing their own success criteria or protected suites in the same approval unit

- Reward hacking and metric fixation
  - use multiple guardrails, adversarial review, qualitative evidence, and periodic metric replacement

- Apparent distress or personhood claims manipulating operators
  - define respectful interaction policy while treating self-reports as outputs, not proof or authority

- Provider-specific behavior leaking into the cognitive core
  - keep commands, flags, event formats, sessions, and auth inside adapters and enforce conformance tests

- A CLI upgrade silently changing permissions or output
  - probe versions and capabilities, pin compatible ranges, fail closed, and require re-evaluation before promotion

- Ambient user credentials giving a subprocess excessive authority
  - launch with a minimal environment, broker scoped credentials, and use isolated service identities where possible

- Provider failover duplicating actions
  - separate planning from execution, use idempotency keys, inspect workspace state, and retry only safe operations

- Multiple providers agreeing on the same false assumption
  - preserve evidence-based verification and avoid treating consensus as correctness

## Open Decisions

1. Should `companions/CodeMeridian.Evolution/` remain permanently co-located, or be extracted after incubation, and which licensing and release rules apply either way?
2. Is the initial deployment single-user/local, team-hosted, or multi-tenant?
3. Which model providers are permitted, and may any source snippets leave the local network?
4. Which UI telemetry events are acceptable under the project's privacy policy?
5. Is draft pull-request creation in the first funded scope, or should v1 stop at patch preparation?
6. Which feature-flag or deployment system will run the first UI experiment?
7. What constitution and terminal goals are authorized, and who may amend them?
8. Which memories should survive model replacement, identity reset, user deletion, or project transfer?
9. Which operational definitions of continuity, self-model accuracy, metacognition, and agency will be preregistered?
10. Who provides independent ethical and scientific review for consciousness-related claims?
11. Are local adapter experiments permitted, or must all learning remain external to model weights initially?
12. Which provider CLIs and versions are supported in the first deployment environment?
13. Should provider processes use user authentication, dedicated service identities, or both?
14. Is ACP the preferred Copilot integration when available, or should the first adapter use subprocess prompts only?
15. Which roles require provider diversity, and when is a cheaper same-provider critic acceptable?
16. May continuation sessions persist across restarts, or should sensitive roles always use fresh ephemeral sessions?

## Success Criteria

- `companions/CodeMeridian.Evolution/CodeMeridian.Evolution.slnx` restores, builds, tests, and packages independently of `CodeMeridian.sln`.
- Architecture tests prevent companion core projects and root CodeMeridian production projects from referencing each other.
- Meridian Evolution boots and passes its core cognitive-ledger acceptance suite with CodeMeridian and all other optional integrations disabled.
- The UI projects only Meridian Evolution state and can rebuild every material view from journal entries and versioned projections.
- A sensor can be registered, health-checked, run, retried, and removed without changing core orchestration code.
- Observations are immutable, deduplicated, redacted, and traceable to their source.
- The agent can resume authorized goals, commitments, and relevant context after a complete process restart.
- Memories retain provenance, confidence, freshness, contradiction state, and correction history.
- The self-model describes only capabilities supported by current evidence and evaluations.
- Every goal traces to an authorized source and remains subject to cancellation and policy.
- Repeated experience can produce a tested reusable skill without changing base-model weights.
- Learned memories, skills, prompts, and policies can be rolled back independently.
- Codex, Claude, Copilot, and the fake provider can satisfy the same provider conformance suite where their declared capabilities overlap.
- Provider routing depends on capability snapshots and evaluation evidence rather than vendor-specific conditionals in the cognitive core.
- A provider can crash, time out, upgrade incompatibly, or become unavailable without losing canonical memory, identity, goals, or approvals.
- Provider failover never broadens permissions or repeats a non-idempotent action.
- Durable records contain bounded decision summaries and evidence, not hidden chain-of-thought.
- A signal and hypothesis can be reproduced from stored evidence.
- No candidate executes without declared metrics, scope, policy, and rollback.
- All important actions require the configured approval.
- One improvement to Meridian Evolution's own ledger, cognition loop, or projection UI completes the full observe-to-outcome cycle.
- Future proposal ranking can use prior outcomes while explaining exactly how that history influenced the result.
- Consciousness-related reports distinguish operational results, model self-reports, interpretations, uncertainty, and counterevidence.
- Installing CodeMeridian improves optional code-intelligence scenarios without changing the companion's identity, core ledger semantics, or standalone availability.
