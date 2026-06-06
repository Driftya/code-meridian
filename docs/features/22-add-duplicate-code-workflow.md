# Add Duplicate-Code Workflow

- Status: done
- Priority: P2
- Note: Once embeddings work, CodeMeridian can turn `find_similar_nodes` into a practical duplicate-code review flow.


**Why:** Once embeddings work, CodeMeridian can turn `find_similar_nodes` into a practical duplicate-code review flow.

**Tasks:**

- Add `find_duplicate_candidates`.
- Group similar methods/classes by score.
- Filter by project, namespace, node type, and size.
- Exclude tests by default.
- Show refactor risk using callers and coverage.

**Effort:** Medium  
**Value:** Medium  
**Risk:** Medium.

**Implemented:** Added `find_duplicate_candidates` for embedded method/class nodes. It filters by project, namespace, node type, minimum line count, similarity threshold, and excludes tests by default. Results include grouped duplicate candidates with similarity, size, fan-in refactor risk, and direct test-caller coverage signals.

