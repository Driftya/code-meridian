# Project Query Compatibility Audit

Date: 2026-10-29. Delivery gates: B04 and D09 in the [capability checklist](plans/2026-10-19-project-capability-completion-checklist.md).

This is an audit ledger for the current generic routing declarations, not a completed compatibility certification. Persisted node/edge kinds and matching relationship names do not establish analyzer coverage or query compatibility. All direct public queries still require current generation/source/producer evidence. The producer audits in A remain independent prerequisites. Legacy service construction without a profile service is an explicit advisory/testing path, not production authorization.

## Current Declarations And Evidence

| Query family | C#/TypeScript/JavaScript | SQL | Frontend | PowerShell |
|---|---|---|---|---|
| Exact resolution / implementation surface | Callable definitions required | Not admitted by callable file-kind declarations | Not admitted by callable file-kind declarations | Explicit-file exact lookup requires `powershell_definitions`; implementation surface remains unsupported |
| Editing context / minimal context / impact | Callable definitions and dependencies; test sections have separate evidence | Not admitted by callable file-kind declarations | Not admitted by callable file-kind declarations | Unsupported pending semantic conformance |
| Connection | Callable definitions and dependencies | Not admitted by connection alternatives | HTML/CSS/SCSS require `frontend_relationships`; source-backed single-colon file IDs now verified by direct-guard fixtures | Unsupported pending semantic conformance |
| Test shields / coverage gaps | Separately require `test_to_code` | No callable compatibility | No callable compatibility | Pester evidence unavailable |
| Namespace / architecture / cycles | Separately require namespace/policy capabilities | No callable compatibility | No callable compatibility | Classes and namespace classification unsupported |
| Projection / similarity / embeddings | Separately require projection/embedding capabilities | Mixed-content projection/filter semantics unaudited | Mixed-content projection/filter semantics unaudited | No generic projection/embedding coverage |
| Configuration definitions / usage | Independent configuration producer and usage evidence | SQL objects do not grant configuration evidence | Frontend relationships do not grant configuration evidence | Scripts do not grant configuration usage evidence |
| Cascade | Not implied by callable/TSX evidence | Not applicable | Separately requires `frontend_cascade`; ordinary frontend relationships do not grant it | Not applicable |
| Inventory constraints / structural Markdown | Independent parser/inventory rule evidence | Inventory placement can apply | Inventory placement can apply | Inventory placement can apply independently of worker support |
| Freshness / drift / profile | Discovery/freshness metadata, without semantic authorization | Same | Same | Same |

## Identifiers, Ownership, And Response Boundaries

C# and SQL producers use double-colon project-qualified IDs. Frontend file/concept IDs use `Project:File:...` and `Project:ExternalConcept:...`. PowerShell file/body identities use `Project::PowerShellFile::...` and `Project::PowerShellBody::...`; files and script/function/filter bodies remain distinct. Generic API enum compatibility alone does not prove callable semantics for SQL or frontend nodes.

Direct guards now recognize the frontend forms, derive the project, resolve the exact ID, confirm that returned ID/project match, and obtain its observed inventory path before evaluating requirements. A wrong returned ID, foreign project, missing source path, or unverified target cannot authorize the query. `DirectCapabilityGuardTests` covers single-colon file success and wrong-ID refusal, existing cross-project refusal, stale generations, and PowerShell methods borrowing no C# readiness. This fixes the known frontend ID routing mismatch without enabling a new capability.

Every returned connection node now also requires matching exact-node identity/type/project/source and observed inventory, with the same current generation. Callable and frontend path families have separate node/relationship allowlists. SQL/PowerShell intermediates cannot borrow callable endpoint coverage, and source-less shared concepts cannot establish inventory ownership. Missing evidence discards the whole path rather than reporting an executed empty finding; evidence failures and cancellation remain distinct. `ConnectionCapabilityBoundaryTests` verifies these boundaries and preserves verified callable paths.

Shared frontend concepts without a file path still cannot establish an inventory boundary. B04 remains open for their ownership evidence, real producer-to-storage-to-query fixtures, other mixed SQL/frontend query and type-filter boundaries, source snippets, and projection/response conformance. The graph repository still computes an unrestricted shortest path; refusing that path does not prove the absence of a longer supported path. Project-wide mixed graphs need the outstanding checks even when an exact non-callable target would be refused. TSX/JSX documents observed as TypeScript/JavaScript also require an explicit audit of overlapping frontend ownership.

## PowerShell Audit Boundary

The isolated worker maps files to `File`, lexical bodies to `Method`, and resolved static occurrences to inferred `Calls` or distinct `DependsOn`. Publication persists source spans and snippets, checks source/worker identity against the complete inventory generation, and owns replacement/deletion. That is persistence evidence, not proof of generic exact/context/impact/connection/snippet semantics.

Profiles advertise only `powershell_definitions`. Complete source/worker-bound full publication, with every selected file complete, can authorize explicit-file `resolve_exact_symbol`; process success without verified publication cannot. Native-worker fixtures cover positive lookup and missing/partial/delta/unbound/mismatched evidence. The fresh-process installed-client smoke checks real persistence and authenticated MCP lookup. Lookup verifies exact source/project, node family, language/analyzer ownership and complete publication status; it excludes retained stale/removed nodes and returns no snippets. Project-wide PowerShell lookup remains unavailable.

`PowerShellPublicationTests.NativePublishedFactsDoNotAuthorizeUnauditedGenericQueries` still proves context/impact/connection/snippet refusal alongside unrelated complete C# evidence. Actual native facts also cannot leak through C# neighbours. Classes, dynamic/data-flow behavior and Pester shields remain unsupported. This is an audit of admitted and refused combinations, without generic PowerShell dependency activation. See the [worker support boundary](../tools/PowerShellIndexer/supports.md).

Impact findings, their intermediate path nodes, and every minimal-context node/snippet now require exact identity/type/source/project, current accepted callable inventory and current coverage. Missing, foreign, source-less, SQL/frontend/PowerShell and changed-generation nodes discard the result. `CallableResultBoundaryTests` first reproduced 18 unauthorized results and preserves valid callable neighbours. Other generic query/filter/projection combinations remain B04 work.
