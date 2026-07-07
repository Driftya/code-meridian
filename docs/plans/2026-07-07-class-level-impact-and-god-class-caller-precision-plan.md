# Class-Level Impact And God-Class Caller Precision Plan

- Status: implemented
- Date: 2026-07-07
- Scope: improve generic class-level caller precision for `find_impact` and `find_god_classes`
- Principle: improve generic structural usefulness first; any framework- or repo-specific caller heuristics must be configurable or backed by indexed metadata instead of hardcoded product behavior

## Why This Plan Exists

Recent real-use review showed a repeated gap:

- `find_god_classes` was useful for narrowing a hotspot list.
- `find_impact` was weak for class-level refactor review in at least one real repo because it produced no useful inbound caller evidence for the selected class.
- The tool experience then fell back to direct source inspection even though the graph already had enough nearby signals to do better.

This is not the same problem as broad result noise. The current gap is that class-level blast radius and class-level fan-in are still too literal about direct graph edges and too weak about the caller shapes that matter during refactor planning.

## Review Findings

### Verified Current Behavior

- `Neo4jCodeGraphRepository.FindImpactAsync(...)` expands the target through `Contains` members and then walks `StructuralTraversalRelationships` backward to those nodes.
- `Neo4jCodeGraphRepository.FindGodClassesAsync(...)` counts direct callers plus callers of contained members and ranks by `(fanIn * 10 + lineCount)`.
- `CodebaseQueryService.FindImpactAsync(...)` returns a simple `"No callers found"` guidance message when the repository returns nothing.
- `CodebaseQueryService.FindGodClassesAsync(...)` renders a useful risk table, but the fan-in explanation is still opaque and does not separate stronger from weaker caller evidence.

### Confirmed Weak Spots

- Repository integration coverage for `FindImpactAsync` is too weak at the generic seam: one test only asserts `"not null"` for a known node instead of characterizing useful class-level callers.
- `find_test_shield` on the repository `FindImpactAsync` and `FindGodClassesAsync` methods currently reports no direct or heuristic shield, which matches the weak contract coverage.
- `find_god_classes` still depends heavily on raw fan-in plus line count, so a broad class can rank highly even when the caller evidence is thin or mostly indirect.
- `find_impact` gives no extra explanation when a class has member activity or workflow usage that exists nearby but does not cross the current query path cleanly.

### Generic Product Gap

The product needs a stronger generic notion of "class is operationally depended on" than only:

- direct edges to the class node
- calls/uses/depends-on edges to member nodes

Without that, class-level refactor planning remains under-powered even when method-level analysis is acceptable.

## Goal

Make class-level refactor review materially more useful by improving:

1. `find_impact` for class/interface targets
2. `find_god_classes` caller evidence and ranking explanation

while keeping the solution generic across arbitrary indexed codebases.

## Non-Goals

- Do not add another broad noise-reduction pass to unrelated tools.
- Do not hardcode ASP.NET, DI container, controller, CLI, or framework-specific rules directly into ranking logic unless they come from generic indexed metadata or explicit configuration.
- Do not regress method-level `find_impact` behavior while improving class-level behavior.
- Do not pretend weak or inferred caller evidence is proven.

## Desired Outcome

When the target is a class or interface:

- `find_impact` should return a more useful class-level blast radius than `"no callers found"` in cases where the graph already contains actionable member, dependency, or workflow-adjacent evidence.
- `find_god_classes` should distinguish stronger class-level fan-in from weaker incidental or indirect fan-in.
- Output should explain whether the risk comes from:
  - direct class callers
  - callers of contained members
  - dependency/construction/composition evidence
  - weaker heuristic or workflow-adjacent evidence

## Proposed Generic Direction

### 1. Split class-level caller evidence into tiers

Instead of one raw fan-in number, model at least these buckets:

- Direct class callers
- Member callers
- Dependency/composition callers
- Heuristic/workflow callers

This allows:

- better ranking
- clearer confidence
- less over-trust in one blended number

### 2. Improve `find_impact` fallback for class/interface targets

When direct backward traversal on a class is sparse, allow a bounded class-aware expansion that can surface:

- callers of contained methods
- constructors or factories that create/use the type
- interface implementers or consumers when the selected target is an interface
- workflow-adjacent callers already represented in the graph

Important constraint:

- This must stay bounded and explicit.
- Output must label inferred or class-expanded callers separately from proven direct callers.

### 3. Re-rank `find_god_classes` using caller quality, not only fan-in volume

Current ranking is dominated by line count plus raw fan-in.

Proposed direction:

- score direct class callers highest
- score diverse member callers next
- score dependency/composition evidence below direct structural callers
- penalize classes whose fan-in is mostly weak or low-actionability evidence

### 4. Explain the evidence in output

`find_god_classes` should render a compact explanation such as:

- "4 direct class callers, 7 member callers, 0 composition callers"
- or "high line count but caller evidence is mostly indirect"

`find_impact` should similarly avoid a flat `"No callers found"` response when class-expanded evidence exists and instead explain what was found and at what confidence.

## Configuration Boundary

This plan should remain generic by default.

If a repo needs framework-specific caller expansion, it must come from configuration or indexed metadata such as:

- file-role metadata
- node-type metadata
- tool/workflow catalog metadata
- explicit analysis settings in `meridian.json`

Examples of acceptable configurable behavior:

- whether dependency/composition callers are included by default for class targets
- weight boosts for specific indexed node families
- whether workflow callers appear inline or in a secondary section

Examples of unacceptable hardcoding:

- special-casing one DI framework
- special-casing one web framework's composition root naming
- special-casing one repository's folder names

## Implementation Seams

Primary seams to change:

- `src/Infrastructure/Graph/Neo4jCodeGraphRepository.Analytics.cs`
  - `FindImpactAsync(...)`
  - `FindImpactPathsAsync(...)`
  - `FindGodClassesAsync(...)`
- `src/Application/Services/CodebaseQueryService.Analytics.cs`
  - `FindImpactAsync(...)`
- `src/Application/Services/CodebaseQueryService.Analytics.Risk.cs`
  - `FindGodClassesAsync(...)`

Likely supporting seams:

- shared actionability/confidence helpers in `CodebaseQueryService`
- analysis options/config for any opt-in caller-expansion or weighting

## Test Gaps To Close First

### Repository Integration Tests

Add characterization coverage for:

- class target with direct class callers
- class target with only member callers
- interface target with implementer/consumer evidence when indexed
- class target with mixed direct and indirect evidence
- god-class ranking that distinguishes two large classes with different caller-quality mixes

### Application Tests

Add formatting and behavior tests for:

- `find_impact` showing class-expanded evidence instead of a flat no-callers message
- `find_impact` still returning the old no-callers guidance when no evidence exists at all
- `find_god_classes` rendering caller-evidence breakdown and confidence wording
- config-driven behavior toggles if any repo-specific or opt-in expansion is introduced

## Success Criteria

- [x] `find_impact` for class targets returns materially better caller evidence on bounded synthetic fixtures that currently look empty or under-modeled.
- [x] `find_god_classes` ranking can distinguish two similarly large classes when one has stronger direct caller evidence.
- [x] Output separates proven direct callers from class-expanded or heuristic caller evidence.
- [x] Method-level `find_impact` behavior does not regress.
- [x] Any framework- or workflow-specific broadening is configurable or metadata-driven, not hardcoded.
- [x] Regression coverage exists at both repository and application layers.
- [x] Docs explain the new evidence buckets and confidence semantics.

## Proposed Phases

### Phase 1. Lock The Current Gap With Failing Tests

- [x] Add repository integration tests that currently reproduce weak class-level caller evidence.
- [x] Add application tests that pin the current unhelpful `No callers found` and opaque god-class fan-in behavior where appropriate.

### Phase 2. Define Class-Level Evidence Model

- [x] Introduce a small internal model for class-level caller buckets.
- [x] Decide which buckets are proven versus inferred.
- [x] Keep the model generic and graph-backed.

### Phase 3. Improve Repository Queries

- [x] Update `FindImpactAsync(...)` / `FindImpactPathsAsync(...)` for bounded class-aware expansion.
- [x] Update `FindGodClassesAsync(...)` to compute richer caller evidence than a single raw count.
- [x] Keep the query bounded so performance does not explode on large graphs.

### Phase 4. Improve Ranking And Formatting

- [x] Update application-layer ranking to use caller-quality-aware signals.
- [x] Update output formatting to explain evidence buckets and confidence.
- [x] Keep compact mode concise.

### Phase 5. Add Configuration Only Where Needed

- [x] If workflow/composition broadening needs tuning, add it through `analysis.*` config.
- [x] Keep defaults generic and conservative.

### Phase 6. Docs And Verification

- [x] Update `docs/features.md` for `find_impact` and `find_god_classes`.
- [x] Update agent guidance only if the behavior contract changes materially.
- [x] Run focused repository + application tests for the changed seams.

## Risks

- Over-expanding class-level callers could reintroduce the broad noise the repo just reduced.
- Mixing direct and inferred caller evidence without clear labels would make the output sound more certain than it is.
- Query changes at the repository layer could increase cost if not bounded carefully.
- Config added too early could create another tuning surface before the generic model is validated.

## Recommended Implementation Order

- [x] 1. Add failing class-target repository tests
- [x] 2. Add application formatting/behavior tests
- [x] 3. Introduce caller-evidence buckets
- [x] 4. Improve repository class-target queries
- [x] 5. Re-rank and reformat `find_god_classes`
- [x] 6. Add config only if a truly repo-specific broadening rule remains necessary
- [x] 7. Update docs and close the plan

## Definition Of Done

- [x] Class-level `find_impact` is more useful on real refactor-planning seams without becoming noisy by default.
- [x] `find_god_classes` ranking and output explain why a class is risky, not just that it is large.
- [x] The generic behavior works without repo-specific hardcoding.
- [x] Any special broadening behavior is expressed through configuration or indexed metadata.
- [x] Focused regression coverage protects the new class-level contract.
