# Prune Related-Knowledge Result Noise

- Status: implemented
- Priority: P2
- Note: Keyword-driven related-doc and related-code results should collapse duplicates and suppress weak lexical soup.

**Problem:** `find_related_knowledge` is useful, but lexical overlap can still produce repeated or low-signal matches that read as broad orientation rather than actionable context.

**Goal:** Keep explainable lexical matches while reducing duplicate or weakly useful results.

**Expected output:**

- Per-source dedupe for repeated document/file hits.
- Stronger default thresholding for low-usefulness keyword overlap.
- Clearer separation between high-confidence matches and weak awareness-only matches.

**Implemented:** `find_related_knowledge` now deduplicates equivalent document/file targets by normalized target identity, merges matched keywords across duplicates, classifies results into primary and awareness-only matches, and suppresses weak default-score lexical soup unless the caller explicitly loosens `minimumSharedKeywords` or `minimumScore`.
