# Add Test Shield Map

- Status: done
- Priority: P2
- Note: Show which tests protect a change path.

**Feature:** codemeridian find_test_shield OrderService.PlaceOrderAsync

**Why Neo4j helps:** Tests are graph nodes connected to production code through call paths, namespaces, file similarity, and heuristics.

**Expected output:**

- Direct test shield, indirect shield, and unshielded paths for the target.

**Implemented:** Added `find_test_shield`, which combines exact target context, caller-path impact, and existing related-test signals to show how well a change path is protected. The output separates direct test callers to the target from indirect shields on caller/path nodes or heuristic matches, then highlights unshielded path nodes where tests should be added before risky behavior changes. TypeScript test files now also contribute direct shield edges from `it(...)` and `test(...)` callback bodies, including chained forms such as `it.only(...)` and `test.each(...)(...)`.
