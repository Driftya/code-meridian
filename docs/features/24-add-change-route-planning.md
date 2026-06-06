# Add Change-Route Planning

- Status: done
- Priority: P2
- Note: Give the AI an ordered edit path instead of a file dump.

**Feature:** codemeridian plan-edit "replace repository pattern in payments"

**Why Neo4j helps:** Neo4j can follow actual dependency direction and shortest paths through interfaces, implementations, callers, and tests.

**Expected output:**

- An ordered edit route: application port, domain service dependency, infrastructure implementation, DI registration, API endpoint, and tests.

**Implemented:** Added the `plan_edit_route` MCP tool. It ranks graph matches for a goal, inspects the anchor node's callers, callees, interfaces, downstream dependencies, impact nodes, and related tests, then returns an ordered route across contracts, application/domain behavior, infrastructure, composition/API entry points, tests, and fallback graph targets.
