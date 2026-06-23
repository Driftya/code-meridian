# Add Changed-Subgraph Analysis

- Status: implemented
- Priority: P2
- Note: Project the neighborhood around changed files and summarize risk, protections, and architecture impact.

**Feature:** `analyze_changed_subgraph`

**Why Neo4j helps:** A raw git diff shows what changed, but a projected graph around those changes can explain what the change touches, how exposed it is, and which tests or boundaries matter.

## Goal

Turn a list of changed files or a git diff into a bounded, language-neutral graph analysis that helps agents and reviewers judge risk before or after an edit.

## Language-Neutral Requirements

- Accept changed nodes from Roslyn and TsIndexer in the same analysis run.
- Use graph relationships that exist across language boundaries, such as `Calls`, `References`, `DependsOn`, `Reads`, `Writes`, and test/document links.
- Avoid assuming that the changed slice belongs to only one runtime or one indexer.

## Expected Output

- Overall risk level for the changed slice
- The highest-risk changed nodes and why they matter
- Related tests and obvious protection gaps
- Architecture violations or smell paths introduced or touched by the change
- Docs, feature notes, or external concepts that should be reviewed alongside the diff

## Example

```text
Changed files:
- InviteEndpoints.cs
- DriftInviteService.cs
- invite-panel.tsx

Change risk: medium
```

The result should explain whether the diff sits on important paths, whether it crosses architecture boundaries, and whether the surrounding test shield is strong enough.

## Implemented

- Added the `analyze_changed_subgraph` MCP/query-service tool.
- The first slice accepts explicit changed file paths and maps them to indexed graph nodes across mixed C# and TypeScript/TSX inputs.
- The report now includes:
  - overall risk
  - highest-risk changed nodes with concrete reasons
  - impacted neighborhood summaries
  - focused test recommendations and protection gaps
  - architecture violations and dependency smell paths touching the changed slice
  - related docs and feature notes to review
- Docs-only and test-only inputs suppress structural risk noise instead of pretending they are production-code edits.
- Git-working-tree and diff-hunk ingestion remain follow-up work; this slice stays explicit-file-list-first.

## Suggested Scope

- Support explicit file lists first, with git-diff helpers added later.
- Project a bounded neighborhood around the changed nodes instead of analyzing the whole graph.
- Reuse existing impact, test-shield, smell-path, and freshness signals where possible.
- Keep the result explainable and review-oriented rather than turning it into a generic dashboard.
