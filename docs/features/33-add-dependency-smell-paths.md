# Add Dependency Smell Paths

- Status: implemented
- Priority: P2
- Note: Surface architecture rule violations as graph paths.

**Feature:** codemeridian find_smell_paths

**Why Neo4j helps:** Rule violations become explainable when the graph can show the exact path that should not exist.

**Expected output:**

- Domain-to-Infrastructure paths, presentation business-rule paths, and other dependency smells.

**Implemented:** Added `find_smell_paths`, a safe-first graph query that returns the shortest forbidden dependency paths across the current architectural rules. It focuses on explainability first: each finding includes the violated boundary, hop count, source and target nodes, and the exact `Calls`/`Uses`/`DependsOn` path that created the smell. The initial rules cover Core, Application, and presentation-to-infrastructure bypasses without broad speculative classification.

## Follow-Up Update: Forbidden-Dependency Presets

Use this feature note for richer forbidden-path checks rather than creating a second path-violation roadmap item.

### Language-Neutral Requirements

- Run over shared cross-language graph edges instead of C#-only namespace rules.
- Support Roslyn and TsIndexer outputs through normalized layer, dependency, and relationship metadata.
- Preserve explainable path output even when the matched path spans multiple languages or external concepts.

### Follow-Up Scope

- Add named architecture presets such as Domain-to-Infrastructure, Application-to-concrete-adapter, and presentation-business-rule paths.
- Support bounded variable-length path queries with rule-specific edge filters.
- Return rule identifiers, path length, and the exact violating path.
- Keep the path formatter deterministic and safe-first so agents can act on the result.
