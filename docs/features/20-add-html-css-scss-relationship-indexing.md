# Add HTML / CSS / SCSS Relationship Indexing

- Status: implemented
- Priority: P2
- Note: Frontend context is not only TypeScript


**Why:** Frontend context is not only TypeScript. In many apps, the important relationship is between components, templates, class names, selectors, and style files. Indexing this would let CodeMeridian answer "what styles affect this element?" or "which templates use this class?" without loading the whole frontend.

**Start with the useful static subset:**

- HTML files: elements, IDs, class names, attributes, and template file nodes.
- CSS / SCSS files: selectors, class selectors, ID selectors, custom properties, imports, and rough rule locations.
- TSX / JSX: `className` string literals and simple template literals.
- Link HTML/TSX class usage to matching CSS/SCSS selector nodes.
- Link stylesheet imports to importing files.

**Suggested graph relationships:**

- `UsesClass`: template/component -> CSS class selector
- `DefinesSelector`: stylesheet -> selector
- `UsesId`: template/component -> ID selector
- `ImportsStyle`: component/file -> stylesheet
- `UsesCssVariable`: rule/template -> CSS custom property
- `DefinesCssVariable`: stylesheet/rule -> CSS custom property

**Do not attempt in the first version:**

- Full CSS cascade resolution
- Specificity conflict analysis
- Runtime class names from arbitrary expressions
- Framework-specific style scoping rules beyond simple, explicit patterns
- Complete SCSS mixin/function evaluation

**Possible later expansion:**

- CSS specificity and override warnings
- Dead CSS selector detection
- Component-to-style impact analysis
- Tailwind class extraction and config-aware lookup
- Angular/Vue/Svelte template support

**Effort:** High  
**Value:** Medium to high for frontend-heavy repos  
**Risk:** High if it tries to model the full cascade; medium if the first version stays static and relationship-focused.

## Implemented

- Added a dedicated `HtmlCssIndexer` worker to the unified `CodeMeridian.Indexer` flow.
- Indexed static relationships from:
  - HTML `class` and `id` usage
  - CSS / SCSS selector definitions
  - CSS custom property definitions and usages
  - local stylesheet imports in HTML, CSS / SCSS, and TSX / JSX
  - static TSX / JSX `className` and `id` usage
- Emitted explicit graph edges for:
  - `UsesClass`
  - `UsesId`
  - `DefinesSelector`
  - `ImportsStyle`
  - `UsesCssVariable`
  - `DefinesCssVariable`
- Reused the shared workspace package for the internal worker contract and client/batch-loading utilities.

## Current Limits

- The current version is static and conservative by design.
- Full cascade resolution, specificity analysis, and dynamic runtime class-name evaluation are still out of scope.
- Near-duplicate spacing/style clustering remains a future follow-up rather than part of this indexing MVP.

