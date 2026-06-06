# Add `codemeridian doctor`

- Status: done
- Priority: P1
- Note: First-run setup has several moving parts: Docker, Neo4j, MCP server, auth, `.env`, `meridian.json`, embeddings, and indexed data


**Why:** First-run setup has several moving parts: Docker, Neo4j, MCP server, auth, `.env`, `meridian.json`, embeddings, and indexed data. A single health command would make setup problems obvious and reduce support friction.

**Goal:** One command that explains whether CodeMeridian is ready to use and what is missing.

**Suggested command:**

```powershell
codemeridian doctor --project MyApp
```

**Example output:**

```text
Server reachable: yes
Neo4j reachable: yes
MCP endpoint reachable: yes
Indexed nodes: 12,482
Call edges: 34,901
Docs indexed: 78
Diagnostics indexed: 14
Graph drift: low
Embeddings: disabled
```

**Tasks:**

- Check configured server URL and auth.
- Check MCP health endpoint.
- Ask backend for Neo4j connectivity status.
- Count indexed nodes, call edges, docs, diagnostics, and projects.
- Report embedding provider status and whether code-node embeddings exist.
- Include graph drift summary when a project is provided.
- Print exact remediation steps for common failures.

**Effort:** Medium  
**Value:** Very high  
**Risk:** Low, mostly around adding backend health/stat endpoints cleanly.

