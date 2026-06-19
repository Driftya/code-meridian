# Add Context Pack Degraded Mode

- Status: implemented
- Priority: P1
- Note: When `build_minimal_context` cannot complete normally, return a useful partial result plus explicit failure reasons.

**Problem:** In the Driftya `ChainService` moderation refactor session, `build_minimal_context` failed even though other tools in the same sequence worked: freshness checks, exact symbol resolution, and impact/test-shield queries all succeeded. That forced a manual fallback despite the graph already containing enough signal to produce at least a bounded partial answer.

**Goal:** Make `build_minimal_context` resilient so it degrades into a smaller, explainable context pack instead of surfacing a generic invocation failure.

**Expected output:**

- A partial context pack that still returns any successful sections such as exact target, likely files, impact summary, or tests already resolved.
- An explicit failure section that says which sub-step failed, for example caller expansion, downstream traversal, test ranking, or token budgeting.
- A machine-parseable degraded-mode marker so session evaluation can distinguish partial success from hard failure.
- Guidance on the next best fallback tool sequence when degradation happens, for example `resolve_exact_symbol` plus `find_impact` plus `find_test_shield`.

**Success criteria:**

- Sessions no longer see opaque `build_minimal_context` failures when enough graph data exists for a partial response.
- `evaluate-session` can separately count full success, degraded success, and hard failure for context-pack requests.
- Agents need fewer manual fallback commands after successful exact-target resolution.

**Implemented:** `build_minimal_context` now degrades across multiple sub-steps instead of only hard-failing. Impact, downstream, coverage-gap lookup, related-test ranking, source-snippet budgeting, and file-path explanation can fail independently while the tool still returns a bounded partial context pack. Degraded responses include a machine-parseable ``context_pack_status=degraded`` marker, list each failed sub-step with its exception type, and point agents at the next fallback sequence. `evaluate-session` now also recognizes `contextPackStatus: full|degraded|failed` on recorded `build_minimal_context` result events so session reports can count bounded success separately from hard failure.
