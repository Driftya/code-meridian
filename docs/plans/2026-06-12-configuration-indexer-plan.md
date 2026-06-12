# Configuration Indexer Plan

Date: 2026-06-12

## Goal

Add a shared configuration indexing capability that can read configuration sources such as JSON, YAML, and `.env` files, normalize configuration keys into a canonical shape, and link those keys to code that consumes them.

The main value is not raw file parsing. The value is making configuration discoverable in the graph so agents can answer questions like:

- where is this config key defined
- what overrides it
- which options class binds to it
- which code reads it directly
- how do `:` and `__` forms of the same key relate

## Why This Work Matters

CodeMeridian already captures structural code relationships, documentation, diagnostics, and derived keyword overlap. Configuration is still mostly invisible unless an agent manually reads files and infers relationships from naming conventions.

That creates gaps in change planning:

- configuration-driven behavior is easy to miss
- environment overrides are hard to trace
- Docker and appsettings values are disconnected from consuming code
- options binding paths are not represented explicitly
- agents cannot safely answer "what uses this setting" without file-by-file guessing

The practical outcome we want is a configuration-aware graph layer that stays deterministic and explainable.

## Goals

- Index configuration keys from JSON, YAML, and `.env` sources.
- Normalize related key syntaxes into a canonical path.
- Preserve the original raw spelling from each source.
- Link configuration definitions and overrides across files.
- Link canonical keys to code that reads them directly.
- Link canonical keys to typed options classes when binding can be inferred.
- Keep format parsing separate from language-specific code extraction.
- Keep the graph explainable: exact source file, raw key, canonical key, and confidence.

## Non-Goals

- No speculative support for every config ecosystem in MVP.
- No attempt to execute templates, Helm charts, or runtime interpolation in MVP.
- No secret value storage beyond what is already committed in indexed files.
- No fuzzy "_ means :" normalization globally.
- No language-specific configuration graph duplicated inside each indexer.
- No replacement for the existing code/document indexers.
- No full policy engine for precedence resolution in MVP.

## Key Design Decision

Build one shared `ConfigurationIndexer`, not one configuration indexer per language.

Reasoning:

- config files are cross-language assets
- `.env`, Docker Compose, JSON, and YAML are not owned by C# or TypeScript
- normalization logic must be centralized
- future formats should plug into one pipeline
- language indexers can contribute typed config-reference extraction without owning config parsing

Recommended ownership:

- `ConfigurationIndexer`: parse files, normalize keys, create config nodes/edges
- Roslyn indexer: discover typed C# configuration reads and binding patterns
- TypeScript indexer: later discover frontend config usage if needed
- shared linker: connect config definitions to code references

## Scope For MVP

### Read These Sources

- `appsettings.json`
- `appsettings.*.json`
- `meridian.json`
- `meridian.sample.json`
- `.env`
- `docker-compose.yml`
- `docker-compose.sample.yaml`
- other repo-local `*.json`, `*.yaml`, `*.yml` files that match configurable include rules

### Detect These Code Patterns

In C#:

- `IConfiguration["A:B"]`
- `configuration["A:B"]`
- `GetSection("A:B")`
- `GetRequiredSection("A:B")`
- `services.Configure<T>(configuration.GetSection("A:B"))`
- `.Bind(configuration.GetSection("A:B"))`
- strongly named options classes already present in the repo such as `Neo4jOptions` and `EmbeddingOptions`

Later:

- TypeScript config wrappers
- frontend env conventions
- Kubernetes and Helm value linking

## Why String Search Alone Is Not Enough

Plain string search can find many direct reads, but it misses or weakens important typed cases:

- section binding to options classes
- wrappers around `IConfiguration`
- config consumed through helper methods
- links between `CodeMeridian:Auth:ApiKey` and an options type rather than only raw string literals

The plan should support both:

- direct string-based references
- typed/bound configuration relationships

## Canonical Key Model

Canonical keys should use colon-separated paths:

- `Neo4j:Uri`
- `CodeMeridian:Auth:ApiKey`
- `ConnectionStrings:Main`

### Normalization Rules

Safe rules for MVP:

- preserve exact raw key from source
- normalize `__` to `:`
- keep canonical keys case-preserving or normalized consistently, but choose one project-wide rule
- trim whitespace
- remove surrounding quotes from parsed keys

Do not globally normalize single `_` into `:`. That is too ambiguous and will create false links.

### Examples

| Raw key | Source type | Canonical key |
|---|---|---|
| `CodeMeridian:Auth:ApiKey` | JSON | `CodeMeridian:Auth:ApiKey` |
| `CodeMeridian__Auth__ApiKey` | `.env` | `CodeMeridian:Auth:ApiKey` |
| `Neo4j__Uri` | Compose env | `Neo4j:Uri` |
| `ConnectionStrings:Main` | JSON | `ConnectionStrings:Main` |

## Graph Model

### Nodes

Suggested new node kinds:

- `ConfigurationFile`
- `ConfigurationKey`

Optional later:

- `ConfigurationSection`
- `ConfigurationValueSource`

### ConfigurationFile Node

Represents a file that defines configuration values.

Example shape:

```text
(:ConfigurationFile {
  id,
  projectContext,
  filePath,
  format,
  sourceHash,
  createdAt,
  updatedAt
})
```

### ConfigurationKey Node

Represents one canonical configuration key within a project.

Example shape:

```text
(:ConfigurationKey {
  id,
  projectContext,
  canonicalKey,
  normalizedKey,
  valueType,
  isSecretLike,
  createdAt,
  updatedAt
})
```

### Relationships

Suggested edges:

- `(:ConfigurationFile)-[:DEFINES_CONFIG { rawKey, rawValuePreview, sourceKind, line? }]->(:ConfigurationKey)`
- `(:ConfigurationFile)-[:OVERRIDES_CONFIG { rawKey, precedenceHint }]->(:ConfigurationKey)`
- `(:CodeNode)-[:READS_CONFIG { rawKey, accessPattern, confidence }]->(:ConfigurationKey)`
- `(:CodeNode)-[:BINDS_CONFIG { optionsType, confidence }]->(:ConfigurationKey)`
- `(:ConfigurationKey)-[:RELATED_TO_OPTIONS]->(:CodeNode)` optional alias direction if needed later

Prefer a small initial edge set:

- `DEFINES_CONFIG`
- `READS_CONFIG`
- `BINDS_CONFIG`
- `OVERRIDES_CONFIG`

## Value Handling

Do not store full secret values in the graph.

Recommended handling:

- keep the raw key
- keep a masked preview or type hint
- mark likely secret-like keys via heuristic names such as `password`, `token`, `apikey`, `secret`
- preserve enough information to reason about config structure without leaking sensitive content

Example:

```text
rawValuePreview = "***"
isSecretLike = true
```

## Source-Type Parsing Strategy

## JSON

Use a JSON reader that walks nested objects and emits canonical keys from the property path.

Examples:

- `{ "Neo4j": { "Uri": "..." } }` -> `Neo4j:Uri`
- `{ "CodeMeridian": { "Auth": { "ApiKey": "..." } } }` -> `CodeMeridian:Auth:ApiKey`

## YAML

Use a YAML reader for:

- Docker Compose env sections
- nested key/value trees
- flat environment-style keys in YAML lists or maps when present

Examples:

- `environment: Neo4j__Uri: bolt://neo4j:7687`
- nested maps in deployment config

## .env

Use a simple env parser:

- `KEY=value`
- ignore comments and blank lines
- normalize `__` to `:`

## Precedence And Override Modeling

The graph should represent that multiple files can define the same canonical key.

MVP does not need perfect runtime precedence simulation, but it should preserve enough metadata to support reasoning:

- source file path
- source kind
- environment-specific file name
- whether the entry looks like an override of an already-defined canonical key

Examples:

- `appsettings.json` defines `Neo4j:Uri`
- `appsettings.Development.json` overrides `Neo4j:Uri`
- `.env` overrides `CodeMeridian:Auth:ApiKey`
- Docker Compose environment defines `Neo4j:Uri`

## Typed Binding Detection

This is where language-specific extractors help.

### C# Patterns To Detect

- `services.Configure<Neo4jOptions>(configuration.GetSection("Neo4j"))`
- `configuration.GetSection("Embedding").Bind(options)`
- `configuration["CodeMeridian:Auth:ApiKey"]`
- wrappers that take a section string and return an options instance

Resulting graph should distinguish:

- direct string reads: exact `READS_CONFIG`
- section binding: exact or high-confidence `BINDS_CONFIG`

### Why This Should Not Live Entirely In ConfigurationIndexer

The config files do not tell you which options class reads them. That knowledge lives in code.

So:

- file parsing belongs in `ConfigurationIndexer`
- typed usage extraction belongs in Roslyn and later TS support
- graph linking belongs in shared infrastructure/application orchestration

## Suggested Architecture

### Application Layer

New services/interfaces:

- `IConfigurationIndexingService`
- `IConfigurationGraphRepository`
- `IConfigurationFileReader`
- `IConfigurationKeyNormalizer`
- `IConfigurationUsageLinker`

Responsibilities:

- coordinate file discovery
- parse configuration sources
- normalize keys
- persist config nodes/edges
- link config keys to code references

### Infrastructure Layer

Suggested components:

- `Neo4jConfigurationGraphRepository`
- `JsonConfigurationReader`
- `YamlConfigurationReader`
- `EnvConfigurationReader`
- `DefaultConfigurationKeyNormalizer`

Responsibilities:

- file-format parsing
- graph upserts
- query support
- safe preview masking

### Indexer Layer

Suggested components:

- `ConfigurationIndexer` as a new tool/project or a new coordinated step in `tools/Indexer`
- Roslyn-side config usage extraction helper
- later TS-side config usage extraction helper

## Where It Should Run

Recommended pipeline:

```text
1. Existing code/document indexers run first.
2. ConfigurationIndexer discovers supported config files.
3. ConfigurationIndexer parses and normalizes config keys.
4. ConfigurationIndexer writes ConfigurationFile and ConfigurationKey graph data.
5. C# config usage extraction writes direct/typed config usage references.
6. Shared linker connects code reads/bindings to canonical config keys.
```

This keeps code structure and config structure separate but linkable.

## Tooling Surface

Potential CLI commands:

```text
codemeridian index . --config
codemeridian config rebuild --project MyProject
codemeridian config verify --project MyProject
```

Potential MCP tools later:

- `find_config_usage`
- `find_config_definitions`
- `find_options_bindings`
- `find_config_overrides`

MVP does not need all of these. A rebuild path plus one or two query tools is enough.

## First Query Wins

The first useful queries should be:

1. `Where is this config key defined and overridden?`
2. `Which code reads or binds this config key or section?`

If those work well, the rest can build on them.

## Phased Rollout

## Phase 1: Graph Model And Normalization

- define `ConfigurationFile` and `ConfigurationKey`
- define repository contracts
- implement canonical key normalization
- add tests for `:` and `__` handling
- decide casing and ID strategy

## Phase 2: File Parsing

- parse JSON config files
- parse `.env`
- parse YAML for Docker Compose-style env sections
- store config file and key definitions in Neo4j
- mask secret-like values

## Phase 3: Code Linking In C#

- detect direct string-based configuration reads
- detect `GetSection`, `Bind`, and `Configure<T>` patterns
- link section reads to canonical keys or prefixes
- add tests for exact and typed links

## Phase 4: Queries And CLI

- add rebuild command support
- add at least one query for config definitions
- add at least one query for code usage
- document confidence levels and limitations

## Phase 5: Extended Formats And Smarter Overrides

- expand YAML support beyond Docker Compose
- evaluate Kubernetes or Helm support
- improve precedence hints
- add TypeScript/frontend config usage later if needed

## Validation Strategy

For each phase:

- keep the graph model explicit and explainable
- add unit tests for normalization and parsing
- add integration tests for Neo4j persistence
- verify that secret-like values are masked
- verify no false `_ -> :` normalization occurs
- verify binding links are distinguishable from direct reads

## Testing Plan

### Normalization Tests

- `CodeMeridian__Auth__ApiKey` -> `CodeMeridian:Auth:ApiKey`
- `Neo4j:Uri` stays `Neo4j:Uri`
- single underscore keys are not rewritten blindly
- quoted env values do not break key parsing

### Parser Tests

- nested JSON object paths flatten correctly
- YAML environment sections flatten correctly
- `.env` parser ignores comments
- duplicate canonical keys across files are preserved as separate defining edges

### Linking Tests

- `IConfiguration["A:B"]` links to `A:B`
- `GetSection("Neo4j")` creates section-level or prefix-aware linkage
- `Configure<Neo4jOptions>(GetSection("Neo4j"))` links `Neo4jOptions` to `Neo4j`
- secret-like keys are marked but not stored in plaintext

### Integration Tests

- config files produce `ConfigurationFile` nodes
- canonical keys are unique per project/key
- multiple files can define or override the same canonical key
- direct reads and binds are queryable from Neo4j

## Risks

### False Positives

Risk:

- string literals that look like config keys but are not config accesses

Mitigation:

- prefer syntactic detection in Roslyn over broad grep
- label confidence on inferred edges

### False Normalization

Risk:

- treating `_` as hierarchy separator globally

Mitigation:

- only normalize `__` in MVP

### Secret Leakage

Risk:

- storing sensitive config values in the graph

Mitigation:

- mask secret-like values
- store preview/type metadata instead of full values

### Scope Explosion

Risk:

- trying to support every config ecosystem at once

Mitigation:

- keep MVP to JSON, YAML, `.env`, and C# bindings

## Acceptance Criteria

- configuration keys can be indexed from JSON, YAML, and `.env`
- `:` and `__` forms of the same key normalize to one canonical key
- single `_` is not treated as a hierarchy separator by default
- config definitions are linked to source files
- direct C# configuration reads can be linked to canonical config keys
- typed section binding can be linked to options classes or section keys
- secret-like values are not stored in plaintext
- the solution uses one shared configuration indexing flow, not duplicated per language
- the graph can answer where a config key is defined and what code uses it

## Recommendation

Proceed with one shared `ConfigurationIndexer` plus language-specific usage extraction helpers.

Do not embed full configuration parsing separately into Roslyn and TypeScript indexers.

The clean shape is:

```text
configuration files
  -> shared ConfigurationIndexer parses and normalizes
  -> graph stores canonical ConfigurationKey nodes
  -> language extractors detect direct and typed usage
  -> shared linker connects config to code
```

That gives CodeMeridian a deterministic configuration graph instead of fragile string-search heuristics.
