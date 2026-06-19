# HTML / CSS / SCSS Indexer Support

This worker is scaffolded and wired into the unified indexer, but it does not yet emit HTML/CSS/SCSS graph relationships.

## Current Scope

- Accepts the same internal batch-file contract used by the TypeScript worker
- Loads shared CodeMeridian worker configuration
- Reports the selected HTML/CSS/SCSS batch size
- Reserves the workspace/package boundary for future walkers and analyzers

## Not Yet Implemented

- HTML element, class, or ID extraction
- CSS / SCSS selector extraction
- Template-to-selector relationship edges
- Stylesheet import analysis
