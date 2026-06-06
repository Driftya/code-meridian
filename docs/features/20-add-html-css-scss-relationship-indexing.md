# Add HTML / CSS / SCSS Relationship Indexing

- Status: pending
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

