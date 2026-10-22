# Repository Constraint Contracts And Placement

The Core contracts and inventory-only placement evaluator implement C01-C02 of the [capability checklist](plans/2026-10-19-project-capability-completion-checklist.md). The public Application/API/SDK/MCP query, Markdown reference extraction/resolution/publication, capability activation, and architecture report composition remain open. Adding these rules does not enable a new public validation command or establish semantic analyzer readiness.

## Configuration

Keep `architecture.path` in `meridian.json` and the existing namespace `name`, `layers`, and `forbiddenDependencies` properties. A policy can add `repositoryConstraints`, or contain only repository constraints when namespace rules do not apply:

```json
{
  "name": "Document placement",
  "repositoryConstraints": {
    "contractVersion": "1.0",
    "groups": [
      { "id": "documents", "paths": ["**/*.md"], "fileKinds": ["markdown"] },
      { "id": "images", "paths": ["**/*.png", "**/*.jpg"] }
    ],
    "placementRules": [
      {
        "id": "document-location",
        "group": "documents",
        "allowedPaths": ["docs/**"],
        "deniedPaths": ["docs/generated/**"],
        "reason": "Keep authored documents under docs.",
        "severity": "error",
        "exceptions": [{ "path": "README.md", "reason": "Repository entry point." }]
      },
      {
        "id": "image-location",
        "group": "images",
        "allowedPaths": ["docs/images/**"],
        "reason": "Keep document assets under docs/images.",
        "severity": "warning"
      }
    ],
    "referenceRules": [
      {
        "id": "document-images",
        "sourceGroup": "documents",
        "allowedTargetGroups": ["images"],
        "requireLocalResolution": true,
        "referenceKinds": ["image"],
        "reason": "Use resolvable local document assets.",
        "severity": "error"
      }
    ]
  }
}
```

The reference rule is validated configuration only. The placement evaluator returns `unsupported` for an applicable reference rule; it cannot certify link resolution from file inventory.

Groups select paths matching any pattern, intersected with optional observed `fileKinds`. An omitted/empty `fileKinds` selects all kinds. Group IDs and rule IDs are case-sensitive; rule IDs must be unique across both families. A file can belong to several groups, and every matching rule is evaluated.

Patterns are repository-relative globs. `*` matches within a segment, `?` matches one non-separator character, and a whole-segment `**` spans zero or more directories (or any suffix at the end). Thus `**/*.md` includes root Markdown files. Forward/backward separators are accepted. Patterns must have normalized segments; absolute paths, `.`/`..`, negation, character classes, braces, and embedded `**` are rejected. Literal spaces are preserved.

Placement applies an explicit exception first, then denies, then allowed locations. The first matching exception records its reason as an `excepted` finding. Deny wins when both allow and deny match. Empty/omitted `allowedPaths` allows any location unless denied; empty/omitted `deniedPaths` denies nothing. A placement rule must specify at least one allow or deny pattern. Exceptions default to empty and require a reason.

Reference rules select `link` and/or `image` occurrences from a source group. They must require local resolution, restrict allowed target groups, or both. Empty/omitted `allowedTargetGroups` imposes no target-group restriction. Resolution and exception execution await the Markdown implementation.

`ArchitectureRuleSetConfiguration.Parse` rejects unknown properties/custom validators, duplicate JSON properties, unknown versions/groups, duplicate IDs, malformed namespace dependencies, invalid patterns, empty policies, and unsupported severities. Severities are `info`, `warning`, and `error`. Namespace policies retain their existing fields and fallback dependency reason, and must declare layers and dependencies. JSON properties retain case-insensitive compatibility. Collections are non-null after parsing; optional rule lists default to empty.

The configuration indexer validates the active JSON policy before clearing or uploading configuration. Invalid/unreadable policies and missing custom paths return exit code 2. An absent conventional `.meridian/architecture.json` retains the legacy unconfigured behavior. The repository loader uses default namespace rules only when neither an explicit path nor an indexed default policy exists; an invalid, empty, missing explicitly configured, oversized, or potentially truncated indexed policy fails the query. Existing configuration previews are only 80 characters, so values of 80 or more characters are conservatively rejected by that legacy loader. This does not provide lossless policy persistence or audited configuration coverage.

## Inventory Evaluation

`RepositoryPlacementEvaluator.Evaluate` consumes a versioned policy and `RepositoryConstraintInventory`; it performs no file reads, semantic parsing, remote fetches, or repository modifications. The caller supplies a positive inventory generation, discovery state (`complete`, `partial`, `unknown`), explicit filesystem case sensitivity, accepted files, and known excluded paths. Excluded directory paths also cover descendants. Case sensitivity is not inferred from the execution host. Callers must establish inventory freshness before presenting results as current.

Input file/target paths normalize separators, repeated separators, and dot segments while preserving case and spaces. Parent segments may resolve within the repository; escaping it, absolute paths, and case-colliding inventory identities are incomplete evidence. A file cannot be both accepted and excluded. The optional target selects one exact file; directory/subtree query support is not claimed.

| Result | Meaning |
|---|---|
| `evaluated` | Placement ran against the selected accepted inventory; findings can still contain violations |
| `not_applicable` | A complete selected inventory contains no members of this rule's group |
| `invalid` | Policy validation failed; no rule evaluation ran |
| `incomplete` | Discovery/target/inventory evidence cannot establish full validation; observed findings may remain |
| `unsupported` | Applicable reference rules need evidence the placement evaluator does not provide |

Coverage is separate from findings. Partial/unknown discovery never produces complete coverage or proves a group absent. Targets are `observed`, `excluded`, `missing` (absent from complete accepted inventory and known exclusions), or `unobserved` (absence cannot be established). Missing, excluded, or unobserved targets do not produce clean validation. Project evaluation covers admitted files only; it does not inspect excluded content.

Version `1.0` outcomes retain generation, rule ID/family, severity, reason, required capabilities, evaluated-file counts, and path/location findings. Placement requires only `repository_inventory`; reference evaluation also requires `markdown_references`. Placement findings distinguish `forbidden`, `misplaced`, and `excepted`. `findingCount` includes exceptions and violations; `findingsTruncated` explicitly marks a bounded sample. The default limit is 100 findings per rule, clamped to 1-1,000, ordered by normalized path. Policy limits are 100 groups, 100 total rules, 32 patterns/exceptions per collection, 2,048 characters per path/pattern, and 512 characters per reason. Inventory files and known exclusions are each bounded at 100,000.
