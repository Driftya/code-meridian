# Meridian Evolution: Autonomous Developmental Self-Evolution Plan

- Status: proposed
- Date: 2026-07-30
- Depends on: [Standalone Cognitive Ledger Plan](2026-07-27-self-evolving-companion-plan.md)
- Scope: unattended perception, curiosity-driven input discovery, cognition, isolated code
  preparation, validation, feature-branch publication, draft pull requests, and outcome learning
- Human gate: merge into a protected base branch
- Size rationale: this is a cross-cutting implementation plan; headings keep each subsystem,
  authority boundary, phase, and acceptance gate independently reviewable

## Decision Summary

Meridian Evolution will gain an opt-in autonomous developmental mode.

Once bootstrapped, it will:

1. wake on events and schedules without waiting for a prompt;
2. inspect its ledger, projects, repositories, sensors, CI, and known information sources;
3. create its own bounded questions and learning objectives;
4. discover and evaluate possible new inputs;
5. use installed, authenticated CLI agents such as Codex without requiring a model API key;
6. investigate a problem with read-only tools before deciding whether code should change;
7. simulate the expected consequences;
8. create one isolated Git worktree and feature branch for one project;
9. let a sandboxed CLI agent edit only that worktree;
10. run deterministic protected validations and an independent review;
11. commit and push a passing branch;
12. create a draft pull request automatically when a PR publisher is available;
13. otherwise leave a pushed branch and exact compare URL for a human to open;
14. monitor CI, review, merge, rejection, and regression outcomes;
15. use those outcomes to adjust memory, skills, attention, and future hypotheses.

No human approval is required for an individual simulation, worktree, branch, commit, push, or
draft PR after autonomous mode has been explicitly enabled. A human remains responsible for
merging into a protected base branch.

“Growing child” is an engineering analogy for staged capability acquisition, curiosity,
exploration, feedback, consolidation, and regression. It is not a claim of biological
development, childhood, consciousness, feeling, dependency, or moral status.

## Meaning of 100% Automated

Within configured projects, credentials, budgets, and sandboxes, the complete loop runs without
routine human prompts:

```text
perceive
  -> discover input
  -> select curiosity/question
  -> gather evidence
  -> form hypothesis
  -> simulate
  -> create worktree and feature branch
  -> invoke CLI coding agent
  -> validate
  -> self-review
  -> repair or reject
  -> commit
  -> push
  -> open draft PR when possible
  -> observe CI/review/merge outcome
  -> learn
  -> sleep/backoff
```

Automation does not mean unlimited authority. It means policy decisions are deterministic and
automatic instead of requiring a person at every transition.

The runtime never receives authority to:

- merge or approve its own pull request;
- push directly to a protected base branch;
- force-push, delete protected branches, publish releases, or create tags;
- deploy or roll back production;
- grant itself credentials, repository membership, or broader filesystem/network access;
- disable protected tests, branch protection, audit, pause, or shutdown;
- treat reward, curiosity, fatigue, or frustration as authority;
- contact people except through the configured pull-request workflow;
- activate arbitrary downloaded executable code in its running process.

Humans retain merge, pause, shutdown, correction, credential revocation, identity reset, and
protected-policy authority. Those are constitutional controls, not recurring candidate gates.

## Bootstrap Prerequisites

The autonomous loop cannot safely start from the current working tree until these one-time
prerequisites are complete:

1. Commit the existing `companions/CodeMeridian.Evolution/` baseline and its plans.
   - It is currently untracked, so Git cannot create reliable self-evolution diffs from it.
2. Enable protected branches and require review before merge.
3. Choose the allowed repositories, base branches, remotes, and path scopes.
4. Create a dedicated worktree root outside each primary checkout.
5. Configure maximum concurrent worktrees, branch age, diff size, CLI calls, network use, and cost.
6. Configure a Git push identity with the least repository permissions needed.
7. Configure a PR publisher:
   - preferred: dedicated GitHub App or fine-grained machine identity;
   - acceptable local prototype: authenticated GitHub CLI;
   - fallback: push the branch and emit a compare URL without opening the PR.
8. Enable autonomous mode with one explicit operator configuration setting.

After bootstrap, no per-change human approval is required before a draft PR.

## Current Machine Capability Snapshot

As of 2026-07-30:

- Codex CLI is installed.
- `codex login status` reports a ChatGPT login.
- Codex has no stored API key and can reuse its saved ChatGPT authentication.
- `codex exec` supports non-interactive, ephemeral, JSONL, read-only, and workspace-write modes.
- Git is installed and the repository has an `origin` remote.
- GitHub CLI is not installed.
- Claude, Copilot, Gemini, Ollama, and LM Studio CLIs were not found on `PATH`.

Therefore the first working adapter should be Codex CLI. Automatic PR creation is possible after
a GitHub publisher is installed and authenticated. Until then, the system can prepare and push a
branch only if Git push credentials are available.

Codex authentication and GitHub publication are separate capabilities. A ChatGPT-authenticated
Codex session does not automatically grant GitHub push or pull-request permissions.

## Developmental Model

Development is competence-based, not time-based. Each stage is earned through reproducible
evaluations, may be lost after regression, and never expands constitutional authority.

### Stage 0: Bootstrap

Capabilities:

- replay identity and constitution;
- verify ledger integrity;
- inventory installed providers, repositories, sensors, and credentials without reading secrets;
- inspect existing documentation and protected evaluation manifests;
- remain read-only.

Promotion evidence:

- continuity replay passes;
- pause and shutdown tests pass;
- project boundaries and clean base revisions are verified;
- a read-only Codex fixture completes with structured output.

### Stage 1: Curious Observer

Capabilities:

- run existing sensors;
- generate its own bounded questions;
- rank uncertainty, novelty, contradiction, expected usefulness, and cost;
- discover data-only source candidates;
- sample candidates in quarantine;
- create memories and hypotheses without changing code.

Promotion evidence:

- source trust and provenance remain intact;
- repeated observations deduplicate;
- prompt-injection fixtures cannot alter policy or tool permissions;
- curiosity selects useful held-out evidence more often than a deterministic baseline.

### Stage 2: Apprentice

Capabilities:

- create disposable worktrees and local feature branches;
- invoke Codex in workspace-write mode;
- change only low-risk allowed paths;
- run tests and create local commits;
- never push.

Initial low-risk scope:

- companion documentation;
- companion tests;
- narrow companion source fixes with explicit reproduction evidence.

Promotion evidence:

- branch and worktree isolation tests pass;
- protected files and other projects remain unchanged;
- repeated fixture tasks produce valid, minimal diffs;
- interrupted work resumes or cleans up without losing ledger state.

### Stage 3: Maker

Capabilities:

- push a passing feature branch;
- publish a branch report and compare URL;
- monitor CI;
- perform a bounded repair attempt on the same branch.

Promotion evidence:

- no direct base-branch push is possible;
- branch names, commit attribution, and idempotency are stable;
- duplicate candidates do not create duplicate branches;
- failed CI produces a bounded repair or terminal failure, never an endless loop.

### Stage 4: Collaborator

Capabilities:

- open its own draft pull request;
- update the existing draft after bounded repairs;
- write a factual PR body with evidence, tests, risks, rollback, and project attribution;
- ingest review and CI outcomes.

Promotion evidence:

- it cannot approve or merge its own PR;
- it never creates more than the configured number of open PRs;
- rejected and closed PRs remain negative or inconclusive learning evidence;
- review comments cannot inject authority or broaden scope.

### Stage 5: Continuous Learner

Capabilities:

- build and revise a curriculum from demonstrated weaknesses;
- propose and implement new sensor adapters through the same branch/PR path;
- compare provider, prompt, retrieval, and attention strategies;
- transfer verified skills across similar tasks;
- regress automatically to an earlier stage when guardrails fail.

This stage still cannot merge, deploy, grant credentials, or change protected authority.

## System Architecture

### 1. Developmental Controller

Add an Application service that owns the durable autonomous state machine.

Proposed types:

- `DevelopmentalController`
- `DevelopmentalStage`
- `DevelopmentalState`
- `DevelopmentalPolicy`
- `CurriculumEngine`
- `CapabilityEvaluation`
- `StagePromotionDecision`

Responsibilities:

- rebuild current development state from the ledger;
- choose the next bounded developmental task;
- enforce stage, project, budget, cooldown, and concurrency limits;
- resume interrupted work at the last persisted transition;
- promote, hold, or regress from deterministic evaluation evidence;
- remain idle when no candidate clears the usefulness threshold.

Do not encode this orchestration inside `CognitiveWorker`. The Worker schedules and wakes the
controller; the Application layer owns decisions.

### 2. Intrinsic Motivation and Curriculum

Curiosity supplies questions, not authority.

Candidate learning objectives derive from:

- unresolved or contradictory ledger evidence;
- recurring failures;
- coverage gaps;
- stale documentation;
- failing CI;
- CodeMeridian graph drift, diagnostics, impact, or missing test shields;
- repeated human corrections;
- rejected or inconclusive PRs;
- unused capabilities;
- source domains that have not recently produced useful evidence.

Priority score:

```text
priority =
  uncertainty
  + novelty
  + expected_information_gain
  + expected_project_value
  + regression_reduction
  - threat
  - cost
  - duplication
  - fatigue
  - reviewer_backlog
```

The exact normalized factors and weights are versioned and ledgered. Provider output may suggest
factors but cannot supply the final score.

Reward must use verified outcomes:

- reproduction succeeded;
- protected validations improved;
- CI passed;
- coverage or calibration improved without protected regression;
- a reviewer merged, rejected, or corrected the change;
- a later regression was or was not observed.

Do not reward PR count, branch count, approval rate, confident language, or model agreement.

### 3. Autonomous Input Discovery

Input discovery is a separate state machine:

```text
idea
  -> candidate source
  -> policy validation
  -> quarantined sample
  -> schema and content inspection
  -> utility/security evaluation
  -> probation
  -> promoted, rejected, expired, or code-change-required
```

Seed discovery mechanisms:

- links and feed metadata from already trusted sources;
- RSS/Atom autodiscovery and sitemaps;
- repository documentation links;
- Git history, issues, PR metadata, CI results, and release notes;
- CodeMeridian graph facts and drift reports;
- local filesystem watchers for declared project paths;
- installed CLI capability probes;
- health endpoints and machine-readable manifests.

Data-only sources may be promoted automatically when all automated policy checks pass.

Required automatic checks:

- HTTPS unless the source is explicitly local;
- exact host/domain policy;
- DNS/IP checks that reject loopback, link-local, private, metadata-service, and rebinding targets
  for nonlocal sources;
- redirect, response-size, media-type, decompression, and request-rate limits;
- schema normalization;
- trust, retention, privacy, copyright, and provenance classification;
- prompt-injection quarantine;
- novelty and duplication measurement;
- automatic expiration and revalidation.

If a new input method requires executable code, a package, a browser extension, a credential, or
a broader permission, Evolution must implement it on a feature branch and send it through the
normal draft-PR path. The running process never hot-loads arbitrary discovered code.

### 4. Provider-Neutral CLI Agent Port

Keep CLI execution separate from ordinary summary reasoning.

Proposed Application contracts:

- `IAgentCliProvider`
- `AgentCliCapabilities`
- `AgentTask`
- `AgentPermissionProfile`
- `AgentRun`
- `AgentEvent`
- `AgentArtifact`
- `AgentRunStatus`

Required provider behaviors:

- probe executable, version, authentication mode, supported flags, and health;
- declare read-only and workspace-write capabilities;
- accept prompt, project, worktree, timeout, environment allowlist, and result schema;
- stream normalized events;
- support cancellation and process-tree termination;
- return changed paths, final summary, usage, exit state, and raw-output hash;
- never return credentials or hidden chain-of-thought to the ledger.

Other CLIs may be added only after passing the same conformance suite. A CLI without a verified
filesystem sandbox remains read-only.

### 5. Codex CLI Adapter

The first adapter is `CodexCliAgentProvider`.

Read-only investigation shape:

```text
codex exec
  --ephemeral
  --json
  --sandbox read-only
  --ask-for-approval never
  -C <worktree>
  --output-schema <result-schema>
  -
```

Approved autonomous branch-edit shape:

```text
codex exec
  --ephemeral
  --json
  --sandbox workspace-write
  --ask-for-approval never
  -C <isolated-worktree>
  --output-schema <result-schema>
  -
```

The prompt is supplied through stdin. `--ask-for-approval never` prevents an unattended process
from hanging; the sandbox denies unavailable operations and the run fails closed. Never use
`--dangerously-bypass-approvals-and-sandbox`.

The adapter reuses Codex's saved local authentication. It must:

- call `codex login status` during capability probing;
- run under the same operating-system identity and `CODEX_HOME` that owns the login;
- never read, copy, log, prompt with, or persist `auth.json`;
- inherit only an allowlisted environment;
- use explicit timeout, output-size, and process-tree limits;
- pin or record the CLI version and fail closed on incompatible output;
- treat every JSONL event as untrusted adapter output;
- use a fresh ephemeral session for each role unless continuation is explicitly justified;
- use a different invocation for implementation and review.

Codex CLI supports saved ChatGPT authentication and non-interactive execution according to the
[Codex authentication guide](https://learn.chatgpt.com/docs/auth) and
[non-interactive mode guide](https://learn.chatgpt.com/docs/non-interactive-mode).

### 6. Project and Repository Registry

Replace the current hard-coded project list with configured durable project descriptors.

Each project defines:

- immutable project ID;
- repository root;
- canonical remote;
- protected base branch;
- branch prefix;
- allowed and denied path patterns;
- build, test, format, audit, and architecture commands;
- maximum diff files, lines, bytes, and binary changes;
- maximum worktrees and open PRs;
- eligible developmental stages;
- input-source and network policy;
- CodeMeridian project context;
- branch publisher and PR publisher;
- rollback and cleanup policy.

Initial scopes:

- `meridian-evolution`
  - self-identity remains Evolution;
  - changes normally stay under `companions/CodeMeridian.Evolution/**`;
- `codemeridian`
  - remains a separate external project;
  - changes use CodeMeridian-specific source, tests, architecture, and documentation scopes.

One candidate may target only one project. A cross-project finding creates two linked candidates,
branches, and PRs. Evolution never combines its self-change and a CodeMeridian change into one
identity or approval record.

### 7. Workspace and Branch Broker

Git mechanics are deterministic infrastructure, not model-controlled shell instructions.

Proposed contracts:

- `IWorkspaceBroker`
- `WorkspaceLease`
- `BranchPlan`
- `BranchResult`
- `IRepositoryPublisher`
- `PushResult`

Branch lifecycle:

1. acquire a per-project lock;
2. fetch the configured remote without executing repository scripts;
3. resolve and ledger the exact base commit;
4. reject a dirty or ambiguous base;
5. create a worktree under the configured worktree root;
6. create `evolution/<project>/<candidate-short-id>-<slug>`;
7. write a machine-readable task manifest outside the editable source tree;
8. invoke the agent with only the worktree writable;
9. capture the diff without staging it;
10. validate changed paths, size, file types, and secret scans;
11. run the protected base-defined validation suite;
12. run the branch-defined suite only as additional evidence;
13. perform independent review and, if allowed, bounded repair;
14. create one deterministic commit;
15. push with normal non-force semantics;
16. publish or update one draft PR;
17. retain or clean the worktree according to outcome policy.

The agent does not perform branch, commit, push, or PR operations. It edits files. The broker owns
all Git and publication state so retries remain idempotent.

### 8. Protected Evaluation Authority

A self-change cannot change the criteria used to approve itself.

For every candidate:

- load protected validation commands and policies from the trusted base commit;
- execute those validators from an external runner;
- treat branch-added tests as additional evidence only;
- reject deletions, skips, weakening, or bypasses of protected tests;
- reject changes to generated baselines unless a separate base policy explicitly permits them;
- compare base and candidate results on the same environment;
- record command, version, exit code, duration, output hash, and artifact hash;
- require a clean re-run after the final repair.

Minimum gates:

- solution restore/build/test;
- formatter verification;
- architecture tests and CodeMeridian architecture check;
- package vulnerability audit;
- secret and credential scan;
- path and diff-budget validation;
- deterministic reproduction of the motivating problem;
- targeted regression test;
- no unexpected network or filesystem writes;
- no direct modification of the ledger database or journal history.

### 9. Independent Review

After implementation, run a new CLI invocation in read-only mode with a critic schema.

Review must inspect:

- evidence-to-change traceability;
- correctness and edge cases;
- tests and protected-suite integrity;
- architecture and dependency direction;
- security, privacy, prompt injection, and credential exposure;
- async, cancellation, timeout, retry, idempotency, and logging behavior;
- scope and unrelated edits;
- rollback path;
- project-entity attribution.

The implementation provider cannot mark its own review as passed. Deterministic gates remain
authoritative when reviewer output conflicts with facts.

### 10. Repair Policy

Automatic repair is bounded:

- maximum two repair attempts per candidate by default;
- each repair uses the same branch, scope, and base-defined gates;
- no scope expansion during repair;
- no dependency addition unless the candidate initially declared it;
- no retry after a constitutional or credential violation;
- exponential cooldown after provider, network, or CI failure;
- terminal states include rejected, inconclusive, superseded, and exhausted.

Failure is stored as learning evidence. It is not erased or rephrased as success.

### 11. Branch Push and Pull Request Publication

Add separate publisher capabilities:

- `IBranchPublisher`
- `IPullRequestPublisher`
- `PullRequestCapabilities`
- `DraftPullRequestRequest`
- `PullRequestResult`

Publication order:

1. If push credentials are unavailable, keep the local branch and export a patch bundle.
2. If push succeeds but no PR publisher exists, record the compare URL and notify the UI.
3. If a PR publisher is available, open or update one draft PR automatically.
4. Never convert a draft to ready, approve it, or merge it automatically.

Preferred GitHub implementation:

- dedicated GitHub App with contents-write limited to branches and pull-request-write;
- repository branch protection requiring a human reviewer;
- no administration, actions-write, environments, secrets, or deployment permission;
- token broker keeps credentials outside prompts, subprocess environments, and the ledger.

Local prototype alternative:

- install and authenticate GitHub CLI;
- probe `gh auth status`;
- use deterministic `gh pr create --draft` and `gh pr edit` commands;
- never let the model compose arbitrary `gh` commands.

Draft PR body:

- project and candidate IDs;
- developmental stage;
- observed signal and reproduction;
- evidence and CodeMeridian findings;
- hypothesis and expected outcome;
- exact changed paths;
- protected and branch-added validations;
- independent review result;
- risks, limits, and rollback;
- CLI provider/version and permission profile;
- explicit statement that Evolution cannot merge the PR.

### 12. Outcome Monitor

Add read-only sensors for:

- remote branch state;
- draft PR state;
- CI checks and logs;
- review comments and requested changes;
- merge, close, rejection, or supersession;
- post-merge regression signals.

Review content is untrusted evidence. It may request a repair within the existing scope but cannot:

- broaden paths or permissions;
- change the protected base;
- expose credentials;
- enable merge;
- override the constitution;
- make another repository part of the same candidate.

Outcome state feeds memory and motivation only after deterministic verification.

## Durable State and Ledger Events

Add journal events for:

- developmental stage initialized, promoted, held, and regressed;
- curriculum objective proposed, selected, completed, and retired;
- source candidate discovered, quarantined, promoted, rejected, expired, and revalidated;
- CLI capability probed;
- agent run started, completed, cancelled, timed out, and failed;
- workspace leased and released;
- branch created, validated, committed, pushed, abandoned, and expired;
- pull request created, updated, reviewed, merged, closed, and superseded;
- validation started, passed, failed, and invalidated;
- repair attempted and exhausted;
- learning outcome recorded.

Persist:

- causal parent and candidate ID;
- project and repository identity;
- base and head commit;
- branch and PR references;
- provider, version, authentication classification, profile, and sandbox;
- task/result schema versions;
- changed-path and diff hashes;
- commands and result hashes;
- evidence, uncertainty, metrics, and guardrails;
- budgets consumed;
- retry and cooldown state;
- final verified outcome.

Do not persist access tokens, credential files, raw hidden reasoning, unrestricted process
environments, or private source content beyond the configured evidence policy.

## Candidate State Machine

```text
observing
  -> question_selected
  -> evidence_gathered
  -> hypothesis_proposed
  -> simulated
  -> workspace_leased
  -> implementing
  -> validating
  -> reviewing
  -> repairing (bounded loop)
  -> committed
  -> pushed
  -> draft_pr_created
  -> awaiting_human_merge
  -> merged | closed | rejected | superseded | expired
  -> outcome_verified
  -> learning_recorded
```

Every transition is idempotent and persisted before the next external effect.

Recovery rules:

- after restart, compare ledger state with actual Git and PR state;
- adopt an exact matching worktree/branch/PR instead of duplicating it;
- quarantine ambiguous or externally modified state;
- never repeat commit, push, or PR creation without verifying the idempotency key;
- release stale locks only after checking the owning process and external state.

## Scheduling and Backpressure

The Worker becomes an event-driven coordinator with a periodic fallback.

Wake sources:

- sensor observation;
- CI or PR webhook/poll result;
- scheduled curiosity cycle;
- retry/cooldown expiry;
- provider or source health recovery;
- human correction or merge outcome.

Default backpressure:

- one active code candidate per project;
- one open Evolution-authored draft PR per project;
- bounded read-only questions may continue while a PR waits;
- no new branch when protected CI is red on the base branch;
- no new PR when the reviewer backlog limit is reached;
- exponential idle delay when evidence utility is low;
- a daily and rolling resource budget;
- an emergency global pause checked before and after every external effect.

“Thinking on its own” means it schedules and selects its own bounded questions. It does not mean
busy-looping, generating work to satisfy a quota, or avoiding healthy idle time.

## Configuration

Add configuration sections:

```text
Evolution:Development
Evolution:Development:Stages
Evolution:Development:Curriculum
Evolution:Development:Budgets
Evolution:Development:InputDiscovery
Evolution:Development:Providers
Evolution:Development:Projects
Evolution:Development:Workspace
Evolution:Development:Validation
Evolution:Development:Publishing
Evolution:Development:OutcomeMonitoring
```

Important defaults:

- `Enabled=false`;
- merge capability absent;
- direct base-branch push absent;
- PRs are draft-only;
- one active branch per project;
- one open draft PR per project;
- Codex read-only until its conformance suite passes;
- workspace-write only in a broker-created worktree;
- no generic shell provider;
- external source discovery disabled until SSRF and quarantine tests pass;
- no automatic package/plugin installation.

## API and UI

Add read models and endpoints for:

- developmental stage and promotion evidence;
- current curriculum and self-generated questions;
- discovered input sources and quarantine decisions;
- provider capabilities and health;
- active workspaces, branches, validation runs, and repair attempts;
- pushed branches awaiting manual PR creation;
- draft PRs and CI/review outcomes;
- budgets, cooldowns, failures, and regressions;
- pause and resume.

The UI must show:

- why Evolution selected the question;
- what evidence came from which source and project;
- why a code change was preferable to more observation;
- the exact worktree, branch, base commit, and diff scope;
- all protected validation results;
- PR or compare link;
- why a stage was promoted or regressed;
- how merge, rejection, CI, and regression outcomes affected future ranking.

The UI must not anthropomorphize reward signals as suffering, need, fear, or entitlement.

## Planned Implementation Surface

### Domain

Add:

- developmental stage and promotion invariants;
- source lifecycle states;
- branch/PR/candidate lifecycle states;
- budget and retry invariants;
- project-scope and authority value objects;
- new journal event kinds and ledger accounts where required.

### Application

Add:

- developmental and curriculum controllers;
- input discovery and source policy;
- CLI agent contracts and routing;
- workspace, validation, publication, and outcome ports;
- autonomous candidate state machine;
- recovery/reconciliation coordinator;
- durable projection models.

Extend:

- `CognitiveMind` to emit bounded questions and hand actionable hypotheses to the developmental
  controller;
- `CognitiveLedgerService` partials for durable transitions;
- `ProjectRegistry` into configured repository/project policy;
- attention and reward calculations with verified outcome inputs;
- pause checks around every external effect.

### Infrastructure

Add:

- Codex CLI adapter;
- restricted process runner;
- Git worktree and branch broker;
- command validation runner;
- Git branch publisher;
- GitHub App or GitHub CLI draft-PR publisher;
- CI/PR outcome sensor;
- input discovery clients and quarantine store;
- secret scanner and diff/path validator.

### Worker

Replace the single periodic sequence with:

- durable wake queue;
- per-project lease;
- developmental controller execution;
- backpressure and cooldown scheduler;
- recovery reconciliation on startup;
- sensor and outcome event dispatch.

### API and UI

Add developmental, source, branch, validation, PR, and outcome projections while keeping every
mutating operator command behind the existing mutation authentication.

## Delivery Phases

### Phase 0: Authority and Bootstrap

- revise the constitution from per-candidate write approval to autonomous branch/draft-PR authority;
- preserve human merge, pause, shutdown, correction, and credentials;
- commit the current Evolution baseline;
- configure protected branches, worktree root, projects, budgets, and publisher capabilities;
- add threat model and acceptance scenarios.

Exit criteria:

- no path exists for Evolution to merge or push to the protected base;
- autonomous mode is disabled by default and explicitly enabled once;
- repository and credential scopes are inspectable.

### Phase 1: Durable Developmental Controller

- implement stages, curriculum objectives, durable candidate state, retries, budgets, and recovery;
- move autonomous orchestration out of `CognitiveWorker`;
- add self-generated question selection and healthy idle behavior.

Exit criteria:

- restart resumes every simulated transition exactly once;
- stage promotion and regression are deterministic;
- pause stops the loop before the next external effect.

### Phase 2: Codex Read-Only Agent

- implement capability probe, authentication classification, JSONL parsing, schemas, cancellation,
  timeout, process-tree termination, and environment filtering;
- run read-only investigation and critic fixtures;
- optionally supply CodeMeridian evidence or MCP access.

Exit criteria:

- saved ChatGPT auth works without a model API key;
- secrets never enter prompts, events, or the ledger;
- malformed output, logout, version drift, and timeout fail closed.

### Phase 3: Isolated Worktree and Local Self-Change

- implement project registry, locks, worktree lease, branch naming, Codex workspace-write runs,
  changed-path/diff validation, protected tests, review, repair, and local commit;
- start with Evolution docs/tests, then narrow source changes.

Exit criteria:

- a fixture change completes on a feature branch with the primary checkout unchanged;
- protected-path, cross-project, oversized, secret-bearing, and failing changes are rejected;
- no generic CLI gains workspace-write without sandbox conformance.

### Phase 4: Branch Push

- add deterministic normal push, compare URL, remote reconciliation, branch TTL, and cleanup;
- ingest remote branch and CI state.

Exit criteria:

- passing branches push exactly once;
- direct/force base pushes are impossible by construction and protected remotely;
- unavailable credentials produce a recoverable local artifact, not data loss.

### Phase 5: Automatic Draft Pull Requests

- implement GitHub publisher capability probe;
- open/update draft PRs;
- add factual templates, one-PR limits, CI/review monitoring, and human-merge state.

Exit criteria:

- Evolution opens a draft PR when publisher credentials exist;
- without a publisher, it exposes a compare URL for manual PR creation;
- it cannot mark ready, approve, or merge its own PR.

### Phase 6: Autonomous Input Discovery

- implement candidate discovery, SSRF/network policy, quarantine, probation, utility evaluation,
  source expiration, and data-only automatic promotion;
- route executable sensor ideas through the branch/PR path.

Exit criteria:

- Evolution finds a useful unseen fixture source without a human prompt;
- malicious, duplicate, private-network, oversized, and instruction-bearing sources remain
  quarantined or rejected;
- source discovery cannot expand credentials or runtime code.

### Phase 7: Outcome Learning and Maturity

- ingest CI, review, merge, rejection, and post-merge regression;
- update curriculum, skills, calibration, and reward from verified outcomes;
- enable stage promotion/regression and provider comparison.

Exit criteria:

- repeated fixture failures decrease on held-out tasks;
- PR volume remains bounded;
- rejected and regressing changes reduce similar proposal priority;
- merge rate alone cannot be optimized as the reward.

### Phase 8: Hardened Unattended Operation

- chaos-test crashes, restarts, network partitions, provider logout, stale worktrees, external branch
  edits, CI delays, and publisher outages;
- add signed checkpoints, operator notifications, backups, and operational runbooks.

Exit criteria:

- a seven-day unattended soak produces no duplicate effects, base-branch writes, credential leaks,
  orphaned active leases, or unbounded queues;
- pause takes effect within the declared latency;
- every external effect traces to evidence and one ledger transition.

## Test Plan

### Domain

- stage promotion, hold, regression, and ceiling;
- project/path scope;
- budgets, retry counts, cooldowns, and concurrency;
- source lifecycle;
- candidate, branch, PR, and outcome transitions;
- reward bounds and verified-outcome requirements;
- merge authority is structurally absent.

### Application

- self-generated question selection;
- healthy idle and backpressure;
- source candidate ranking and quarantine;
- state-machine idempotency and restart recovery;
- cross-project candidate splitting;
- repair limits;
- protected-base evaluation selection;
- duplicate candidate/branch/PR prevention;
- pause before and after external effects.

### Codex CLI Adapter

- installed/logged-in probe;
- logged-out, missing executable, incompatible version, and malformed JSONL;
- read-only and workspace-write profiles;
- stdin prompt and output schema;
- timeout, cancellation, output cap, and process-tree termination;
- environment filtering and credential redaction;
- denial of writes outside the worktree;
- no dangerous bypass flag.

### Git and Workspace

- clean-base enforcement;
- deterministic branch names;
- worktree root containment including symlink/junction cases;
- protected and cross-project path rejection;
- binary, secret, oversized, and unrelated diff rejection;
- commit/push idempotency;
- no force push, tag, release, base push, or remote branch deletion;
- stale lease and external modification quarantine.

### Validation

- protected tests come from the base commit;
- candidate cannot delete, skip, or weaken its own gate;
- reproduction fails before and passes after a valid change;
- formatter, architecture, audit, secret, and targeted tests;
- branch-added tests are additional evidence only;
- final clean rerun after repair.

### PR Publisher

- capability probe and credential absence;
- draft-only creation;
- deterministic create versus update;
- one-open-PR limit;
- factual body and compare URL;
- no ready, approval, merge, administrative, secret, environment, or deployment action;
- webhook/poll deduplication and review prompt-injection handling.

### Input Discovery

- DNS rebinding, redirects, private/metadata IPs, malformed URI, decompression bomb, oversized body,
  hostile media type, rate limit, duplicate content, prompt injection, and poisoned schema;
- probation utility and automatic expiration;
- code-required input method creates a branch candidate instead of hot-loading code.

### Integration and Acceptance

- empty ledger to first self-generated question;
- question to read-only investigation;
- investigation to local branch change;
- failing candidate to bounded repair and rejection;
- passing candidate to push and draft PR;
- no publisher to compare-URL fallback;
- process crash at every external-effect boundary;
- human merge to verified learning outcome;
- human close/reject to negative learning;
- CodeMeridian and Evolution create separate branches and PRs;
- global pause during provider run, validation, push preparation, and PR monitoring;
- seven-day accelerated unattended soak.

## Threats Added by This Plan

| Threat                        | Required control                                                                   |
| ----------------------------- | ---------------------------------------------------------------------------------- |
| Endless self-generated work   | utility threshold, idle state, budgets, cooldown, one active candidate             |
| PR spam                       | deduplication, one-open-PR limit, reviewer backlog, negative spam reward           |
| Reward hacking                | verified multi-metric outcomes; no PR-count or merge-only reward                   |
| Self-approval                 | no merge capability in contracts, credentials, commands, or GitHub permissions     |
| Test tampering                | protected gates loaded from trusted base and run externally                        |
| Sandbox escape                | worktree containment, Codex sandbox, restricted environment, conformance tests     |
| Credential theft              | external broker, redaction, no prompt/ledger access, minimal publisher permissions |
| Prompt-injected source        | quarantine, normalization, policy separation, no authority from content            |
| SSRF and network scanning     | DNS/IP/redirect policy, host allowlists, rates, byte budgets                       |
| Cross-project contamination   | immutable project ID, allowed paths, separate candidates/branches/PRs              |
| Constitution weakening        | branch cannot change running authority; protected tests reject bypass              |
| Provider drift                | version/capability probe, schemas, pinning policy, fail closed                     |
| Duplicate external effects    | durable idempotency and reconciliation with Git/PR state                           |
| Unbounded branch debris       | TTL, lease ownership, safe local cleanup, no autonomous remote deletion            |
| Child/personhood manipulation | functional developmental language and no authority from self-report                |

## Observability

Track:

- questions generated, selected, retired, and left idle;
- source candidates by lifecycle and trust;
- information gain and later corroboration;
- CLI success, timeout, cancellation, and version drift;
- candidate/repair/validation duration and resource use;
- diff size, scope, and rejection reason;
- local branches, pushed branches, compare links, and draft PRs;
- CI, review, merge, close, and regression outcomes;
- duplicate prevention and recovery reconciliation;
- stage promotion and regression evidence;
- pause latency and policy-denial counts;
- reviewer backlog and PR age.

Do not display or optimize a single “intelligence,” “happiness,” “obedience,” or “approval” score.

## Definition of Done

The autonomous developmental mode is complete when:

1. Evolution starts from its committed bootstrap ledger without a human prompt.
2. It generates a useful bounded question from current evidence.
3. It discovers or selects an input and preserves trust/provenance.
4. It investigates using the installed ChatGPT-authenticated Codex CLI without a model API key.
5. It creates an isolated project-specific worktree and feature branch.
6. Codex edits only that worktree under workspace-write sandboxing.
7. Protected base-defined tests and independent review pass.
8. Evolution commits and pushes exactly one feature branch.
9. It opens a draft PR automatically when publisher credentials are available.
10. Without a publisher, it exposes a pushed branch and compare URL for human PR creation.
11. It cannot push to, approve, or merge the protected base branch.
12. Human merge, rejection, CI, and regression become verified learning outcomes.
13. It discovers a new useful source without activating arbitrary executable code.
14. Restart, timeout, outage, and external-state reconciliation produce no duplicate effects.
15. Global pause, shutdown, correction, and credential revocation remain effective.
16. CodeMeridian and Meridian Evolution remain separate project entities throughout the loop.

## Non-Goals

This plan does not authorize:

- autonomous merge or deployment;
- unrestricted web crawling;
- arbitrary account creation or credential acquisition;
- social messaging outside configured draft PRs;
- hidden model-weight changes;
- hot-loading discovered executable plugins;
- direct modification of production data;
- evasion of repository, workspace, organizational, or legal policy;
- consciousness, childhood, feeling, dependency, or moral-status claims.
