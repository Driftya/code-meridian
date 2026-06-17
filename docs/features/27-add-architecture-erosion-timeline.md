# Add Architecture Erosion Timeline

- Status: done
- Priority: P2
- Note: Track how architecture gets worse over time.

**Feature:** codemeridian architecture drift-history

**Why Neo4j helps:** Update timestamps and re-index counts let the graph act as a living history of architectural erosion.

**Expected output:**

- A time series of cross-layer references, cycles, and god-class growth over the last 30 days.

**Implemented as:** `architecture_drift_history`

The timeline uses current graph findings plus indexed node timestamps. Resolved or deleted historical violations are not recoverable until CodeMeridian stores historical edge snapshots.
