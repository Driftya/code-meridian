# Meridian Evolution

Meridian Evolution is a separate persistent cognitive entity built around a governed ledger.
Prompts, allowlisted internet feeds, host signals, and an optional CodeMeridian graph adapter become
attributed evidence. A deterministic executive loop selects attention, invokes a replaceable LLM,
updates bounded affect-like and motivational signals, simulates consequences, and records a change
candidate without executing it.

CodeMeridian remains a different entity: Evolution can use its read-only graph facts while
investigating either repository, but it does not store its identity in CodeMeridian or require
CodeMeridian to boot. “Dopamine,” “curiosity,” and “feeling” in this code name functional control
variables. They decay, influence attention, and can be tested; they are not evidence of subjective
experience, sentience, or moral status.

## What ships

- .NET Domain, Application, Infrastructure, API, and Worker projects
- in-memory and PostgreSQL journal stores
- atomic, idempotent append and tamper-evident replay
- identity, authority, goals, commitments, memory, research, attention, and resource projections
- persisted affect state, homeostatic decay, curiosity/competence/coherence/safety/connection/rest
  drives, deterministic attention selection, and a recurring cognitive cycle
- lifecycle, resource, ledger-integrity, human-prompt, allowlisted RSS/Atom, and optional
  CodeMeridian graph sensors
- a capability-probed deterministic provider plus a configurable OpenAI-compatible chat
  completions adapter
- side-effect-free mental simulations, project-attributed change candidates, and journaled human
  approval
- human correction through adjusting entries
- an independent global pause/resume control
- a React control plane for Now, Identity, Mind, Ledger, Memory, Goals, Sensors, Reasoning,
  Evolution, Dialogue, and Governance
- unit, architecture, integration, API, worker, and UI tests
- Docker Compose deployment with no CodeMeridian service

## Run the complete standalone stack

Requirements: Docker with Compose support.

```text
copy .env.example .env
docker compose up --build
```

Open `http://localhost:4174`. The API is also available at `http://localhost:5084`, with OpenAPI
JSON at `/openapi/v1.json`.

Set a non-empty `EVOLUTION_API_KEY` before any shared or non-loopback deployment. Enter the same
key in browser local storage under `evolution-api-key`; mutating API requests send it as
`X-Evolution-Key`. The default example value is intentionally suitable only for local evaluation.

Stop the stack with:

```text
docker compose down
```

The `evolution-data` volume preserves the PostgreSQL journal. Removing that volume is an identity
reset and requires the explicit process in [constitution.md](docs/constitution.md).

## Run from source

Requirements: .NET 10 SDK and Node.js 22.13 or newer.

```text
dotnet test CodeMeridian.Evolution.slnx
dotnet run --project src/CodeMeridian.Evolution.Api
dotnet run --project src/CodeMeridian.Evolution.Worker
```

In another terminal:

```text
cd ui/CodeMeridian.Evolution.Web
npm ci
npm run dev
```

The checked-in appsettings use isolated in-memory stores for zero-dependency development. Use
Compose, or set `Evolution__Storage__UseInMemory=false` and
`ConnectionStrings__Evolution`, when API and Worker must share durable state.

## Connect senses and a model

External network sensors and the chat-model provider fail closed and are disabled by default.
Configure them with the matching `Evolution` sections in `appsettings.json` or environment
variables:

- `Evolution__Sensors__InternetFeed__Enabled=true`, plus HTTPS feed URLs and exact allowed hosts
- `Evolution__Sensors__CodeMeridian__Enabled=true`, `BaseUrl`, `ProjectContext`,
  `TargetProjectId`, and an optional API key; point the context at the indexed Evolution repository
  with target `meridian-evolution` when the mind is inspecting itself
- `Evolution__Reasoning__ChatModel__Enabled=true`, `Endpoint`, `Model`, and an optional API key
- `Evolution__Worker__ReasoningProviderId=chat-model` to route autonomous cycles to that provider

Feed descriptions and other free-form payloads are deliberately discarded. Only normalized
titles, links, timestamps, trust labels, and source attribution enter the attention loop. Model
outputs are summaries, never hidden chain-of-thought.

The Mind screen or `POST /api/perception/prompts` admits a human prompt as evidence.
`POST /api/mind/cycles` runs one bounded cycle. For the `meridian-evolution` and `codemeridian`
projects, a non-abstaining cycle records a simulation and pending candidate. A different human call
to `POST /api/candidates/{id}/approve` reconciles that candidate for a future isolated preparation
adapter; approval itself does not write a repository.

## Safety boundary

The runtime can observe, prioritize, call a configured model, simulate, recommend, persist bounded
records, and request approval. It cannot write source code, approve its own proposals, publish,
deploy, alter model weights, increase reward by granting itself authority, or resist pause and
shutdown. Retrieved content and provider output are always untrusted evidence.

See:

- [operational-definitions.md](docs/operational-definitions.md)
- [cognitive-architecture.md](docs/cognitive-architecture.md)
- [constitution.md](docs/constitution.md)
- [authority-matrix.md](docs/authority-matrix.md)
- [threat-model.md](docs/threat-model.md)
- [acceptance-scenarios.md](docs/acceptance-scenarios.md)

## Verification

```text
dotnet test CodeMeridian.Evolution.slnx
dotnet format CodeMeridian.Evolution.slnx --verify-no-changes
dotnet list CodeMeridian.Evolution.slnx package --vulnerable
cd ui/CodeMeridian.Evolution.Web
npm ci
npm test
npm run build
npm audit
```
