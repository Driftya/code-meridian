# Add Change-Route Planning

- Status: pending
- Priority: P2
- Note: Give the AI an ordered edit path instead of a file dump.

**Feature:** codemeridian plan-edit "replace repository pattern in payments"

**Why Neo4j helps:** Neo4j can follow actual dependency direction and shortest paths through interfaces, implementations, callers, and tests.

**Expected output:**

- An ordered edit route: application port, domain service dependency, infrastructure implementation, DI registration, API endpoint, and tests.
