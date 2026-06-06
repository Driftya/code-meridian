# Improve Test Discovery and Coverage Context

- Status: done
- Priority: P1
- Note: `find_coverage_gaps` is useful, but `build_minimal_context` needs better test relevance


**Why:** `find_coverage_gaps` is useful, but `build_minimal_context` needs better test relevance. A context pack should identify tests that call the target, nearby tests by namespace/file, and missing tests.

**Tasks:**

- Add a repository query for tests related to a node.
- Include direct test callers when available.
- Fall back to namespace/file-name similarity.
- Mark heuristic matches clearly.

**Effort:** Medium  
**Value:** High  
**Risk:** Medium, because test relationships can be incomplete without better call resolution.

**Implemented:** Added `FindRelatedTestsAsync` to the graph repository and included it in `build_minimal_context`. Context packs now show direct test callers separately from heuristic test matches, include test files in the likely-file list, and keep nearby coverage gaps for missing-test context. Heuristic matches use test namespace/file detection plus namespace, file-name, and node-name similarity, and are labeled explicitly. Neo4j now stores indexed normalized fields (`nameNormalized`, `namespaceNormalized`, `filePathNormalized`, `projectContextNormalized`) so case-insensitive test and diagnostic queries avoid per-row `toLower(...)` work.

