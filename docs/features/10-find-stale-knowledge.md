# Find Stale Knowledge

- Status: done
- Priority: P1
- Note: CodeMeridian persists knowledge across sessions, so it needs a way to detect when remembered docs, manually ingested nodes, external concept links, or agent notes may be stale


**Why:** CodeMeridian persists knowledge across sessions, so it needs a way to detect when remembered docs, manually ingested nodes, external concept links, or agent notes may be stale. Without this, persistent memory can quietly turn into misleading memory.

**Suggested tool:** `find_stale_knowledge`

**Example prompt:**

```text
@copilot What CodeMeridian knowledge might be stale?
```

**Signals to include:**

- Linked external concepts point to missing or deleted code nodes
- Documentation mentions method or class names that no longer exist
- Manual relationships target renamed nodes
- Agent memory is older than the last major reindex
- High number of orphaned nodes

**Output example:**

```text
Possibly stale knowledge:

- ADR-004 references PaymentGateway.ChargeAsync, but the node was renamed.
- External concept "orders table" is linked to old OrderRepository.SaveAsync.
- 12 manual relationships target nodes not updated in 30 days.
```

**Relationship guidance:**

- Prefer a weak `Mentions` or `References` edge from knowledge documents, agent notes, and external concepts to code nodes.
- Keep the relationship directional from knowledge to code so the fact source stays explicit.
- Query it in reverse when needed; the graph can still traverse incoming edges from a code node to find related knowledge.
- Do not model vague semantic similarity as a hard dependency.
- If a knowledge item points at a deleted or renamed node, mark it as stale rather than silently rewiring it.

**Implementation notes:**

- Include the last reindex time and the target node's current existence in the check.
- Surface orphaned docs and notes separately from likely-renamed references.
- Treat stale external concepts as a soft warning, not an automatic deletion.
- Keep the result explainable so the assistant can tell the user why something looks stale.

**Effort:** Medium  
**Value:** High  
**Risk:** Medium, because the stale check needs enough metadata to avoid false positives.

**Implemented:** Added `find_stale_knowledge` to surface unresolved doc references, orphaned external concepts, old notes, and orphaned code nodes. Documents can now carry weak `relatedNodeIds` metadata, which is stored as `Mentions` edges from `KnowledgeDocument` nodes to current `CodeNode` targets when available.

