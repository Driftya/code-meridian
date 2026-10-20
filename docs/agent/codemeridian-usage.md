# CodeMeridian Usage

Use CodeMeridian before guessing from filenames or broad file scans.

Read `get_project_profile(projectContext, targetPath?)` before choosing a language-specific edit workflow. Observed file kinds are independent of enabled analyzers and indexed coverage. Markdown/text targets use document context; do not resolve their filenames or headings as code symbols. `plan_context_workflow` can select `document_review` and reports blocked/unknown readiness when coverage is missing. Published discovery alone, a successful process exit, or a local cache does not prove complete semantic evidence. PowerShell is detected but currently unsupported. Keep code-specific pre-edit checks for callable code targets.

## When To Use CodeMeridian

Use graph tools for:

- architecture and repository orientation
- callers, callees, and impact analysis
- cross-project dependencies
- dead-code checks
- coverage gaps
- feature implementation planning
- frontend HTML/CSS/SCSS relationship analysis
- size and refactor-risk analysis
- exact symbol resolution before edits

Use terminal search for:

- exact file content after the graph narrows the area
- runtime logs
- generated output
- external command results

## Required Pre-Edit Flow

Inspect each tool's applicability before treating its output as analysis. `ready` authorizes a planned step; `ok` marks an executed query, including a legitimate empty result. Unknown, not-indexed, disabled, unsupported, partial, stale, and failed statuses are not clean checks. Required unavailable steps block a workflow; omitted optional steps remain explained. Use canonical IDs returned for the target's actual project, and do not use root-only outcomes for project-wide relationship queries. When evidence is unavailable, continue bounded profile/freshness inspection and local source work with the limitation stated; do not automatically index or run scripts.

For code edits:

1. Use `query_codebase` or `resolve_exact_symbol` to identify the exact target.
2. Use `get_context_for_editing` before editing a method or class.
3. Use `find_impact` before refactors or risky changes.

Do not guess callers or blast radius from file names or grep output.

## Trigger Rules

| Situation | Tool to call |
|---|---|
| Unfamiliar file or project area | `get_architectural_overview` |
| Need the target node ID | `query_codebase` |
| Need the canonical symbol before editing | `resolve_exact_symbol` |
| Before editing a method or class | `get_context_for_editing` |
| Before suggesting a deletion | `find_unreferenced` |
| Before starting feature work | `analyze_feature_implementation_path` |
| Need exact files for a feature/fix | `find_implementation_surface` |
| "How do X and Y relate?" | `find_connection` |
| Frontend component/template/stylesheet relationship work | `build_minimal_context`, then `find_connection` or `find_implementation_surface` |
| Frontend class/selector rename or delete risk | `find_impact` |
| CSS/SCSS cascade or specificity questions | `find_frontend_cascade_conflicts` |
| CSS value duplication or token extraction work | `find_duplicate_candidates` |
| Starting work in a risky area | `find_hotspots` |
| Writing new tests | `find_coverage_gaps` |
| Reviewing recent work | `find_recently_changed` |
| Integrating with a DB or API | `link_external_concept` |
| Asking for largest files, methods, or classes | `find_large_nodes` |
| Asking for god classes or high-coupling risks | `find_god_classes` |

## Node ID Guidance

Node IDs use canonical graph IDs such as:

- `Class:MyNamespace.UserService`
- `Method:MyNamespace.UserService.SaveAsync(User,CancellationToken)`
- `Interface:MyNamespace.IRepository`

If the ID is unclear, resolve it before editing.

## Persisting Observations

When you find something worth remembering, store it with `ingest_document`.

Good examples:

- hidden circular dependencies
- high-risk high-fan-in methods
- major testing gaps
- architectural constraints discovered during implementation

For frontend work, prefer the generic tools first and use `find_frontend_cascade_conflicts` only when the question is truly about cascade behavior rather than ordinary impact or connection analysis.

Example:

```text
ingest_document(
  content: "<finding>",
  source: "copilot-observation",
  projectContext: "<project>"
)
```
