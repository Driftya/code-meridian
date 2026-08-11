# TypeScript and JavaScript Indexer Support

This file is the quick reference for what the shared TypeScript/JavaScript indexer currently parses and indexes.

## Supported Inputs

- `.ts`
- `.tsx`
- `.js`
- `.jsx`

Project discovery recognizes both `tsconfig.json` and `jsconfig.json`. JavaScript-only repositories without either file are indexed from the requested repository root. Dependency, cache, and generated-output directories—including `node_modules`, `.meridian`, `dist`, `build`, and `coverage`—are excluded.

## Supported Syntax

- `class`
- `interface`
- `enum`
- `constructor`
- class `method`
- class `property`
- top-level `function`
- top-level function-valued variables such as `const run = () => {}` and `const run = function () {}`
- `type` aliases, indexed as interface-like nodes

## What The Indexer Produces

- File nodes for each indexed `.ts`, `.tsx`, `.js`, or `.jsx` file
- Module nodes derived from the relative file path
- Type nodes for supported classes, interfaces, enums, and type aliases
- Member nodes for constructors, methods, properties, and supported top-level callable declarations
- `Contains` edges from files to top-level declarations and from classes/interfaces to members
- `Calls` edges for resolvable invocation expressions inside supported callable bodies
- `Uses` edges for resolvable type references, imports, and exported type dependencies
- `DependsOn` edges for relative file imports
- `Implements` and `Inherits` edges for local class/interface heritage
- API endpoint, configuration, database-tracing, and synthetic test-case nodes from the dedicated TS walker passes

## Supported Language Features

- Arrow functions assigned to top-level variables
- Function expressions assigned to top-level variables
- Cross-file imported function-call resolution for named, aliased, namespace, default, and barrel re-export cases
- Interface-typed member calls that resolve to local interface member nodes
- Type-only imports for resolvable local types
- Class inheritance and interface implementation
- Source snippets and source hashes for indexed code nodes
- Repo-configured database tracing for Prisma, Knex, and Neo4j Cypher through `.meridian/database-tracing.json`

## JavaScript Limitations Compared With TypeScript

- JavaScript does not provide TypeScript's declared type information, so `Uses`, typed-member `Calls`, `Implements`, and some inheritance relationships can be less complete.
- JSDoc may improve compiler inference, but JSDoc-derived types are not guaranteed to become first-class graph nodes.
- ES module imports and exports have the strongest cross-file resolution. CommonJS `require`, `module.exports`, and runtime-computed module paths do not currently receive dedicated dependency extraction.
- Dynamic property access, monkey patching, prototype mutation, dependency injection, and values produced at runtime remain conservative, unresolved, or indeterminate.
- Structural indexing does not enable JavaScript type checking. Discovering `jsconfig.json` does not by itself enable `checkJs`; compiler and lint diagnostics remain a separate project-level concern.
- JSX receives code relationships from this indexer and static class, ID, and stylesheet relationships from the HTML/CSS indexer. These are complementary graph passes over the same source file.

## Notes

- The indexer is syntax-first and uses `ts-morph` for conservative symbol-assisted resolution where available.
- Calls and type references use the same v2 relationship-health outcomes as C#: resolved local, external/unindexed, unresolved local, and indeterminate. Duplicate emitted edges are counted separately.
- Full and normal incremental batches record full-catalog evidence per TypeScript or JavaScript project root. Incremental emission remains limited to changed files, while project discovery loads unchanged source files only into the resolution catalog. A bounded partial reason is persisted if tsconfig/jsconfig, discovery, or catalog loading falls back.
- Compiler and lint diagnostics are owned by the unified indexer's project-level diagnostics phase, so `--skip-diagnostics` is honored consistently and stale ordinary diagnostics can be replaced and verified once per project.
- Type aliases are currently represented as `Interface` nodes to stay compatible with the shared graph contract.
- Class accessors, namespace declarations, and nested/local function expressions are not yet guaranteed to be indexed as first-class nodes.
- If a declaration form is not listed above, it is currently not guaranteed to be indexed as a first-class node.
