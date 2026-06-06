# Add Index Verification Command

- Status: done
- Priority: P1
- Note: Exact symbol lookup is much more useful when the user can quickly verify that the local working tree and graph agree before starting implementation.


**Why:** Exact symbol lookup is much more useful when the user can quickly verify that the local working tree and graph agree before starting implementation.

**Goal:** Add a CLI command or flag that runs drift/freshness checks from the indexer side and exits with a non-zero code when graph drift is too high for exact implementation targeting.

**Suggested commands:**

```powershell
codemeridian check-drift --project MyApp
codemeridian check-drift --project MyApp --fail-on high
codemeridian index --verify --project MyApp
```

**Example output:**

```text
Graph drift: low
Missing files: 0
Invalid line ranges: 2
Missing timestamps: 0
Recommendation: graph is safe for exact implementation targeting.
```

**Tasks:**

- Add `codemeridian index --verify` or `codemeridian verify`.
- Add `codemeridian check-drift --fail-on low|moderate|high` for CI.
- Compare indexed file paths and line metadata against the current working tree.
- Report missing files, invalid line ranges, and missing timestamps.
- Recommend `codemeridian index . --project <ProjectName> --clear` when drift is moderate or high.
- Return stable exit codes so CI can fail only on the configured drift threshold.
- Keep it fast enough for pre-work checks and CI.

**Effort:** Medium  
**Value:** High  
**Risk:** Low to medium, mostly around local path mapping when the MCP server runs remotely.

