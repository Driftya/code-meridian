# Roslyn Indexer Support

This file is the quick reference for what the Roslyn indexer currently parses and indexes.

## Supported Syntax

- `namespace` and file-scoped `namespace`
- `class`
- `interface`
- `struct`
- `record class`
- `record struct`
- `enum`
- `delegate`
- `constructor`
- `method`
- `local function`
- `property`
- `field`
- `event` field declarations
- `event` declarations with accessors
- `indexer`
- `operator`
- `conversion operator`

## What The Indexer Produces

- File nodes for each indexed `.cs` file
- Namespace containment edges
- Type nodes for the supported type declarations above
- Member nodes for the supported member declarations above
- `Contains` edges from containers to members
- `Calls` edges for invocation expressions inside supported member bodies
- `Uses` edges for referenced types in parameters, properties, fields, events, indexers, delegates, operators, and type inheritance/base lists

## Supported Language Features

- Static methods
- Static abstract interface members
- Partial classes and partial methods
- File-scoped namespaces
- Positional record declarations
- Expression-bodied members
- Lambda bodies inside indexed member syntax

## Notes

- The indexer is syntax-driven. It does not require semantic compilation to walk a file.
- Type resolution is name-based and intentionally conservative when multiple candidates share a name.
- If a member form is not listed above, it is currently not guaranteed to be indexed as a first-class node.

