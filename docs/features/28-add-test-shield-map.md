# Add Test Shield Map

- Status: pending
- Priority: P2
- Note: Show which tests protect a change path.

**Feature:** codemeridian find_test_shield OrderService.PlaceOrderAsync

**Why Neo4j helps:** Tests are graph nodes connected to production code through call paths, namespaces, file similarity, and heuristics.

**Expected output:**

- Direct test shield, indirect shield, and unshielded paths for the target.
