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
- Call and type-reference candidates are classified as resolved local, external/unindexed, unresolved local, or indeterminate. Framework/package targets are not promised as graph nodes and do not reduce local relationship confidence.
- Duplicate resolved candidates and synthetic member-implementation edges are reported separately from the mutually exclusive raw-candidate outcomes.
- Each index run stores bounded, deterministic unresolved-local and indeterminate samples plus v2 relationship-health totals. Incremental passes use the full C# resolution catalog even when only changed file-owned nodes are ingested.
- Call evidence includes receiver kind/type hints, value-parameter arity, explicit generic arity, `params` metadata, and extension receiver metadata. Safe syntax-only receiver inference covers predefined receivers, explicit lambda/anonymous-method parameters, typed member access, `foreach`, `catch`, declaration patterns, casts, parentheses, null-forgiving access, conditional access, and object creation.
- Unqualified calls prefer the exact declaring type and then exact indexed local base/interface types. A possible member inherited from an unindexed external base is classified as indeterminate; unrelated name-only candidates are not selected for `this`/`base` calls.
- If a member form is not listed above, it is currently not guaranteed to be indexed as a first-class node.
