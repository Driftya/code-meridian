---
name: codemeridian-frontend
description: Route frontend HTML/CSS/SCSS work through CodeMeridian's generic frontend-aware tools first, then use cascade and style-duplicate analysis only when the question is truly frontend-specific.
---
# CodeMeridian Frontend Skill

Use this skill when working in a repository indexed by CodeMeridian and the task touches frontend markup, selectors, stylesheets, CSS variables, or style cleanup.

The goal is to make frontend work use the indexed HTML/CSS/SCSS relationship graph instead of falling back to file-by-file guessing.

projectContext can be found in meridian.json in field project.

## When To Use

Use this skill when the request includes words or intent like:

* frontend
* HTML
* CSS
* SCSS
* selector
* stylesheet
* class rename
* what styles affect this
* what markup uses this class
* component and stylesheet relationship
* cascade
* specificity
* duplicate style values
* style token cleanup

Also use this skill when the edit target is a frontend component, template, markup file, stylesheet, or selector-related behavior.

## Core Rule

Prefer generic CodeMeridian tools first.

Use dedicated frontend-specific analysis only when the question is not a good fit for the existing generic surfaces.

Frontend-aware generic tools should remain the default for:

* impact
* implementation surface
* minimal edit context
* connection tracing
* duplicate analysis when the question is still "what is similar"

Use frontend-specific tools only for questions like cascade conflicts or selector-specific style cleanup.

## Frontend Graph Signals

Expect useful answers from indexed frontend edges such as:

* `UsesClass`
* `UsesId`
* `DefinesSelector`
* `ImportsStyle`
* `UsesCssVariable`
* `DefinesCssVariable`

Treat these as first-class relationships when analyzing frontend work.

## Workflow

### 1. Check Graph Freshness

Before trusting exact class, selector, or stylesheet relationships, verify graph freshness.

Prefer:

* `check_graph_freshness`
* `find_graph_drift`

If freshness is stale or unknown, say so before making rename, delete, or impact claims.

### 2. Build Frontend Context First

Start with the same generic workflow used for backend code, but expect frontend relationships to appear in the result.

Prefer:

* `analyze_feature_implementation_path` for feature requests or `docs/features/*.md`
* `build_minimal_context`
* `find_implementation_surface`
* `resolve_exact_symbol`
* `get_context_for_editing`

Use these to answer questions like:

* what files are likely part of this frontend change
* what markup and stylesheets sit on the same path
* which class, selector, or CSS variable relationships are already indexed

### 3. Use Generic Relationship Tools For Most Frontend Work

Prefer the existing generic tools before frontend-specific ones.

Use:

* `find_connection` for component/template/selector relationships
* `find_impact` before renaming or deleting classes, selectors, imports, or CSS variables
* `find_implementation_surface` when the user asks where to make a frontend change
* `build_minimal_context` when the user needs the smallest edit-ready context pack

Report the actual frontend signals involved:

```text
Frontend signals:
- class usage
- selector definition
- stylesheet import
- CSS variable usage
```

### 4. Use Frontend-Specific Tools Only When Needed

When the question is uniquely frontend-specific, use the dedicated analysis tools.

Prefer:

* `find_frontend_cascade_conflicts` for specificity, override, and shadowing questions
* `find_duplicate_candidates` with the frontend declaration mode when the user asks about near-duplicate CSS values, token extraction, or repeated style drift

Do not jump straight to cascade analysis for a simple rename or impact question.

### 5. Separate Proven Structure From Inferred Styling Behavior

Frontend graph relationships can prove structural connections, but some style reasoning is intentionally inferred.

Use wording like:

* "The graph directly links this markup file to the selector through `UsesClass`."
* "The graph directly shows this stylesheet import."
* "The cascade warning is inferred from indexed specificity and source-order metadata."
* "This does not prove full browser runtime overlap."

Do not present bounded cascade analysis as browser-accurate certainty.

### 6. Recommend The Smallest Safe Edit Route

For frontend changes, prefer this order:

1. locate the markup/component and stylesheet edit surface
2. inspect impact on connected classes, selectors, imports, and CSS variables
3. inspect duplicate or cascade risks only when relevant
4. update the smallest affected frontend files
5. run the narrowest frontend or mixed test/build command that validates the change

## Output Template

Use this compact report before editing:

```text
Graph freshness:
- Status:
- Notes:

Frontend context:
- Main files:
- Main selectors/classes/variables:
- Frontend signals:

Likely edit surface:
- Primary targets:
- Secondary targets:

Impact / relationship checks:
- Generic tools used:
- Direct graph facts:
- Inferred frontend behavior:

Frontend-specific analysis:
- Cascade conflicts:
- Duplicate style candidates:

Tests or verification:
- Frontend tests/build checks:
- Mixed backend/frontend checks:

Risks / unknowns:
- Stale graph risk:
- Runtime-only styling risk:
- Missing coverage:
```

## Guardrails

### Do

* prefer generic tools first
* call out actual frontend edges when they appear
* use dedicated frontend tools only for uniquely frontend questions
* separate proven graph structure from inferred cascade behavior
* keep the edit surface small and explainable

### Do Not

* treat every frontend question as a new CSS-only workflow
* ignore frontend edges that generic tools already expose
* claim full browser-accurate cascade reasoning
* guess class or selector impact from filenames alone
* fall back to broad file scans before trying graph lookup

## Failure Mode

If CodeMeridian cannot provide enough frontend context:

1. Say which graph query was missing or stale.
2. Fall back to narrow repository search.
3. Inspect only the nearby component/template/stylesheet files.
4. Recommend re-indexing if the frontend graph appears outdated.
5. Continue only with clearly stated assumptions.
