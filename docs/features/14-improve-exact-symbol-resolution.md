# Improve Exact Symbol Resolution

- Status: done
- Priority: P1
- Note: CodeMeridian should be able to move from "this is probably the right file" to "this is the exact method/class ID to edit" more often


**Why:** CodeMeridian should be able to move from "this is probably the right file" to "this is the exact method/class ID to edit" more often. Today, implementation-surface lookup can still find broad layers when exact method IDs are missing, stale, duplicated, or not indexed with enough source precision.

**Goal:** Make graph lookup behave more like an implementation navigator, not only an architectural map.

**Tasks:**

- Add an exact symbol lookup path that accepts method/class names, file paths, and line hints.
- Return canonical node IDs alongside every implementation-surface result when available.
- Detect when a file has graph nodes but no method/class near the requested line or concept.
- Report "exact", "file-only", "heuristic", or "stale" target confidence.
- Improve indexer coverage for nested classes, partial classes, local functions, top-level functions, overloads, generated-file exclusions, and language-specific edge cases.
- Add a `codemeridian index --verify` or equivalent drift check that compares graph nodes against the current working tree before implementation work.
- Add integration tests that prove exact method IDs can be found after indexing this repository.

**Effort:** Medium to high  
**Value:** Very high  
**Risk:** Medium, because stable IDs and language-specific symbol models can affect existing graph references.

**Implemented:** Added `resolve_exact_symbol`, which resolves symbol, file, and line hints to canonical node IDs with `exact`, `file-only`, `heuristic`, or `stale` confidence. `find_implementation_surface` now includes canonical IDs and target confidence in its result table. `CodeGraphQuery` supports file-path filtering so exact symbol lookup can query Neo4j directly by indexed file path instead of filtering after a broad result limit. The remaining CLI-level verification path is implemented as `codemeridian check-drift` and `codemeridian index --verify` below.

