# Add Frontend Relationship Awareness To Existing Tools

- Status: done
- Priority: P2
- Note: Frontend relationship data exists, but the current tool experience still leans heavily toward code-only interpretation.

**Problem:** The HTML/CSS/SCSS indexer can now ingest frontend relationships, but existing CodeMeridian tools do not necessarily treat those nodes and edges as first-class query surfaces. If every frontend question becomes a new CSS-only tool, the platform becomes less coherent and less language-agnostic.

**Goal:** Integrate frontend relationship data into existing generic tools wherever that produces a clear, language-agnostic experience. Add dedicated frontend tools only for questions that are genuinely unique to CSS/HTML semantics.

**Preferred direction:**

- Extend existing generic tools first:
  - `find_impact`
  - `find_connection`
  - `find_implementation_surface`
  - `build_minimal_context`
  - duplicate/similarity-oriented analysis
- Let those tools surface frontend edges such as:
  - `UsesClass`
  - `UsesId`
  - `DefinesSelector`
  - `ImportsStyle`
  - `UsesCssVariable`
  - `DefinesCssVariable`

**Questions existing tools should ideally answer after this slice:**

- what files are affected if this class/selector changes
- how does this component connect to these stylesheet rules
- what markup and style files are in the same impact path
- which implementation surface is relevant for a frontend style change

**Only add dedicated tools for truly frontend-specific queries such as:**

- unused declared CSS classes
- used but undeclared classes
- style-token candidate reports
- specificity/conflict reports

**Design rule:**

- Make the default experience more generic and language-agnostic.
- Use dedicated CSS/HTML tools only when the underlying question is not a good fit for an existing generic analysis surface.

**Acceptance criteria:**

- Existing generic tools can include frontend nodes/edges in a useful, non-noisy way.
- Tool output remains explainable and does not collapse into CSS-only jargon by default.
- Any new frontend-specific tool has a clear reason it could not be expressed cleanly through an existing generic tool.

## Implemented

- Expanded the generic Neo4j traversal used by `find_impact`, `find_connection`, `find_downstream`, `get_context_for_editing`, `build_minimal_context`, and related repository analytics to traverse frontend edges:
  - `UsesClass`
  - `UsesId`
  - `DefinesSelector`
  - `ImportsStyle`
  - `UsesCssVariable`
  - `DefinesCssVariable`
- Kept the experience generic instead of adding CSS-only wording:
  - `find_connection` now calls out frontend signals in the discovered path when class/selector/import/variable edges are involved.
  - `get_context_for_editing` and `build_minimal_context` now inherit real frontend callers/callees from the graph instead of silently missing those edges.
- Improved `find_implementation_surface` for frontend work by expanding from frontend concept matches into connected markup and stylesheet files, so class/selector-oriented goals surface edit-ready file targets instead of only abstract concept nodes.
- Added regression coverage at both layers:
  - application tests for frontend connection summaries and implementation-surface expansion
  - Neo4j integration tests for frontend connection, impact, and edit-context traversal

## Notes

- No new frontend-only MCP tool was added in this slice.
- The duplicate/similarity workflow remains embedding-driven and method/class-oriented for now; this change focused on the generic relationship-based tools that can cleanly consume frontend edges today.
