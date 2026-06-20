# Add Frontend Style Near-Duplicate Clustering

- Status: done
- Priority: P2
- Note: Static HTML/CSS/SCSS indexing is in place, but it does not yet help consolidate visually similar style values.

**Problem:** The current HTML/CSS/SCSS indexer can connect markup, selectors, imports, and CSS variables, but it does not yet group spacing or style values that are effectively the same. Frontend-heavy repos often accumulate small visual drift such as `1rem`, `0.98rem`, `1.02rem`, or repeated card variants with nearly identical declarations.

**Goal:** Add graph-backed, explainable clustering for near-duplicate frontend styles so CodeMeridian can surface consolidation candidates without pretending to be a full browser engine.

**Implemented:** Extended `find_duplicate_candidates` so `nodeType=ExternalConcept` clusters indexed CSS declaration concepts by normalized value shape instead of only comparing code embeddings. The HTML/CSS/SCSS indexer now persists declaration-level frontend concepts with selector/property/raw-value metadata, and the duplicate-analysis output reports explainable near-duplicate style clusters across arbitrary properties, numeric unit families such as `px`/`rem`/`em`, color values, and symbolic CSS values.

**First useful slice:**

- Normalize comparable style values for a bounded set of properties:
  - `margin*`
  - `padding*`
  - `gap`, `row-gap`, `column-gap`
  - `border-radius`
  - `font-size`
  - `line-height`
- Support a bounded first set of units:
  - `px`
  - `rem`
  - unitless `line-height`
  - CSS variables as symbolic values when numeric normalization is not possible
- Group declarations into near-duplicate clusters within configurable tolerances.
- Keep recommendations explainable with affected selectors, files, raw values, normalized values, and suggested token/base-class extraction opportunities.

**Expected output:**

- A generic analysis surface that can answer:
  - which selectors share near-equivalent spacing values
  - which clusters look like tokenization candidates
  - which selectors appear to be card/panel variants with nearly identical declarations
- Explainable output with:
  - cluster reason
  - normalized comparison basis
  - affected selectors/files
  - confidence/tolerance notes

**Important constraints:**

- Do not automatically rewrite styles.
- Do not attempt full cascade-aware visual equivalence in the first version.
- Prefer generic duplicate-analysis primitives over CSS-only bespoke scoring when possible.

**Tool direction:**

- First try to extend existing generic duplicate-analysis/reporting surfaces so frontend style clusters can participate in the same product story as code duplication.
- Only add CSS-specific tools where the query cannot be expressed clearly through existing generic duplicate or similarity workflows.

**Acceptance criteria:**

- CodeMeridian can cluster near-duplicate spacing/style declarations within bounded tolerances.
- Results include the selectors and files that make each cluster actionable.
- Recommendations stay explainable and deterministic.
- The feature does not require full cascade resolution to be useful.
