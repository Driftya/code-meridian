# Add Dependency Smell Paths

- Status: pending
- Priority: P2
- Note: Surface architecture rule violations as graph paths.

**Feature:** codemeridian find_smell_paths

**Why Neo4j helps:** Rule violations become explainable when the graph can show the exact path that should not exist.

**Expected output:**

- Domain-to-Infrastructure paths, presentation business-rule paths, and other dependency smells.

**Implemented:** Added `find_smell_paths`, a safe-first graph query that returns the shortest forbidden dependency paths across the current architectural rules. It focuses on explainability first: each finding includes the violated boundary, hop count, source and target nodes, and the exact `Calls`/`Uses`/`DependsOn` path that created the smell. The initial rules cover Core, Application, and presentation-to-infrastructure bypasses without broad speculative classification.
