# Add Refactor Extraction Candidates

- Status: implemented
- Priority: P2
- Note: Find tightly connected groups that are good extraction targets.

**Feature:** codemeridian suggest_extractions

**Why Neo4j helps:** A good extraction usually looks like a dense internal cluster with weak external coupling.

**Expected output:**

- Candidate extractions with move-from location, reason, and nearby tests.

**Implemented:** Added `suggest_extractions`, a GDS-backed safe-first formatter that ranks Louvain communities as extraction candidates. The first slice combines natural modules with hotspot and god-class anchors, nearby related tests, and coverage-gap signals so each candidate explains where the extraction would come from and how protected it is before a refactor.
