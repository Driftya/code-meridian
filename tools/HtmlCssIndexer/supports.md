# HTML / CSS / SCSS Indexer Support

This file is the quick reference for what the HTML / CSS / SCSS indexer currently parses and indexes.

## Supported Inputs

- `.html`
- `.css`
- `.scss`
- `.tsx`
- `.jsx`

## What The Indexer Produces

- File nodes for indexed markup, component, and stylesheet files
- External concept nodes for CSS classes, CSS IDs, CSS selectors, CSS custom properties, and CSS declarations
- `UsesClass` edges from HTML / TSX / JSX files to CSS class concepts
- `UsesId` edges from HTML / TSX / JSX files to CSS ID concepts
- `DefinesSelector` edges from stylesheet files to selector concepts
- `UsesClass` edges from selector concepts to targeted CSS class concepts
- `UsesId` edges from selector concepts to targeted CSS ID concepts
- `ImportsStyle` edges from markup, component, and style files to local imported stylesheet files
- `DefinesCssVariable` edges from stylesheet files to defined CSS custom property concepts
- `UsesCssVariable` edges from selector concepts to referenced CSS custom property concepts
- `Uses` edges from selector concepts to declaration concepts with `relationshipKind=DefinesStyleDeclaration`

## Supported Static Extraction

- HTML `class="..."` and `id="..."`
- HTML `<link rel="stylesheet" href="...">` for local stylesheet files
- CSS / SCSS selectors with class and ID targets
- CSS / SCSS `@import`, `@use`, and `@forward` for local stylesheet files
- CSS custom property definitions such as `--brand: ...`
- CSS custom property usage such as `var(--brand)`
- CSS declarations with selector, property, and raw value metadata
- CSS selector specificity metadata, same-stylesheet source-order metadata, and inferred override edges for shared class/ID targets
- TSX / JSX static `className="..."`
- TSX / JSX simple template-literal `className` values when the literal pieces are statically visible
- TSX / JSX static `id="..."`
- TSX / JSX local stylesheet imports such as `import './Card.scss'`

## Notes

- The first version is intentionally static and relationship-focused.
- Dynamic class expressions are indexed only when the string pieces are statically visible.
- Cascade analysis is intentionally bounded: specificity and source-order reasoning is inferred within a stylesheet, not full browser-accurate cascade emulation.
