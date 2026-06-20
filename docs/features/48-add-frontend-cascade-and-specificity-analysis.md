# Add Frontend Cascade And Specificity Analysis

- Status: pending
- Priority: P3
- Note: Relationship indexing is implemented, but override reasoning is still intentionally out of scope.

**Problem:** The current frontend indexer can show class usage, selector definitions, imports, and CSS variable usage, but it cannot explain which selector likely wins, where specificity conflicts exist, or when one rule shadows another. That limits impact analysis for style cleanup and rename/delete confidence.

**Goal:** Add a bounded, explainable form of cascade and specificity analysis that is useful for impact questions without trying to fully emulate the browser.

**First useful slice:**

- Compute selector specificity for indexed CSS/SCSS selectors.
- Record source-order metadata within a stylesheet.
- Identify likely override/conflict relationships for selectors that target the same class or ID concept.
- Surface explainable warnings for:
  - stronger selector overrides
  - later rule overrides within the same stylesheet
  - suspiciously specific selectors when simpler selectors already exist nearby

**Expected output:**

- Generic impact/context surfaces should be able to say:
  - this selector is likely shadowed by a more specific selector
  - these rules target the same class but appear in override order
  - deleting or changing this selector likely affects these weaker/stronger related selectors

**Important constraints:**

- Do not build a full browser CSS engine.
- Do not model every framework-specific scoping rule in the first version.
- Mark uncertain or partial cascade reasoning explicitly as inferred, not proven.

**Tool direction:**

- Prefer enriching existing impact and explanation tools with specificity/cascade metadata where possible.
- Add CSS-specific tools only for questions that cannot fit existing generic impact or duplicate-analysis surfaces cleanly.

**Acceptance criteria:**

- Indexed selectors carry enough metadata for bounded specificity reasoning.
- CodeMeridian can surface likely override/conflict relationships with clear explanations.
- Results clearly distinguish proven structural relationships from inferred cascade behavior.
