# TypeScript Indexer Support

This file is the quick reference for what the TypeScript indexer currently parses and indexes.

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

- File nodes for each indexed `.ts` or `.tsx` file
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
- Type-only imports for resolvable local types
- Class inheritance and interface implementation
- Source snippets and source hashes for indexed code nodes
- Repo-configured database tracing for Prisma, Knex, and Neo4j Cypher through `.meridian/database-tracing.json`

## Notes

- The indexer is syntax-first and uses `ts-morph` for conservative symbol-assisted resolution where available.
- Type aliases are currently represented as `Interface` nodes to stay compatible with the shared graph contract.
- Class accessors, namespace declarations, and nested/local function expressions are not yet guaranteed to be indexed as first-class nodes.
- If a declaration form is not listed above, it is currently not guaranteed to be indexed as a first-class node.
