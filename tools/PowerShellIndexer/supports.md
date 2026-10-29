# Offline PowerShell Worker

Status: the [capability checklist](../../docs/plans/2026-10-19-project-capability-completion-checklist.md) tracks the isolated parser, owned publication, and opt-in CLI integration. Production profiles report the worker as supported and disabled by default. Complete verified publication can establish only `powershell_definitions`: exact definition lookup with an explicit observed file path. Context, impact, connection, snippets, classes, data flow and Pester queries remain unsupported.

## Parser Compatibility Decision

The worker targets .NET 10 and exactly pins [System.Management.Automation 7.6.8](https://www.nuget.org/packages/System.Management.Automation/7.6.8). `packages.lock.json` locks its transitive dependency graph for Windows/Linux x64. The parser dependency remains in this executable project; Core, Application, and the MCP server do not reference it.

The compatibility spike found that public `Parser.ParseInput` performs post-parse symbol resolution. Its `using module` resolver constructs a PowerShell runspace and invokes module discovery. See the pinned [parser](https://github.com/PowerShell/PowerShell/blob/v7.6.8/src/System.Management.Automation/engine/parser/Parser.cs) and [symbol resolver](https://github.com/PowerShell/PowerShell/blob/v7.6.8/src/System.Management.Automation/engine/parser/SymbolResolver.cs). Consequently the worker does not use public `ParseInput` or `ParseFile`.

`PowerShellSyntaxParser` verifies the loaded 7.6.8 build and reflects the pinned internal `Parser.Parse(..., ParseMode.ModuleAnalysis)` signature. This mode skips post-parse symbol/type/module resolution. An absent/incompatible version, mode, signature, or result fails explicitly; there is no fallback to default parsing. This internal API choice requires a new compatibility review and package tests for upgrades. It reports native lexer/grammar diagnostics, not full post-parse semantic validation or PowerShell execution validity.

DSC configuration processing can create a runspace inside the grammar itself. A conservative decoded-text preflight rejects case-insensitive `configuration` occurrences before grammar processing, including occurrences in comments, names, literals, and expandable-string subexpressions. `using assembly` text is likewise rejected before the grammar can probe assembly paths. These restrictions intentionally produce incomplete evidence for some otherwise valid scripts. A native lexical preflight also rejects configuration tokens, and each parse uses an empty temporary dynamic-keyword table.

No runspace, pipeline, source script, profile, module, assembly directive, manifest evaluator, build hook, or linked script is invoked by the final worker. Manifest values use a small AST-literal reader rather than `SafeGetValue`, `Import-PowerShellDataFile`, or expression compilation. Non-execution tests include observable source/module/manifest/default-parameter/profile side effects and inspect the using-module resolver's thread-local field on the parsing thread.

## Running The Standalone Worker

Create a UTF-8 JSON request with an absolute root and explicit path case semantics:

```json
{
  "contractVersion": "1.0",
  "project": "Example",
  "rootPath": "C:/repositories/example",
  "caseSensitive": false,
  "files": ["scripts/main.ps1", "scripts/helpers.ps1", "modules/tools.psm1"]
}
```

Use the actual root/case value for the selected filesystem. Paths are canonical repository-relative `.ps1`, `.psm1`, or `.psd1` files; explicit selection does not claim complete repository inventory. Unknown request fields, unsupported versions, missing case semantics, duplicate/colliding paths, traversal, absolute source paths, and unsupported extensions are rejected. Observed source/root/ancestor reparse boundaries are rejected. Missing/unreadable sources retain failed file records without facts.

```text
dotnet run --project tools/PowerShellIndexer -- --describe-worker
dotnet run --project tools/PowerShellIndexer -- analyze request.json
```

The self-contained executable accepts the same arguments. `--describe-worker` reads worker/parser/Core implementation bytes and reports package/build/runtime identity without reading repository source. Analysis writes a version 1.0 JSON report to stdout. Exit 0 means the selected extraction/binding pass has no reported gaps; exit 2 means partial/failed file or binding evidence, or cancellation; exit 1 means invalid protocol/input setup. Every report retains `coverage: "unknown"`; process success does not authorize generic queries. Reported parser assembly version and package version are distinct facts.

## Extracted Facts And Identities

Each successfully decoded source is parsed from its single byte buffer and carries that buffer's SHA-256 fingerprint. Encoding is strict UTF-8 without BOM, or BOM-declared UTF-8/UTF-16 LE/BE/UTF-32 LE/BE. Invalid encodings and NUL/BOM-less UTF-16/32 inputs fail without guessed code pages. File extensions do not trigger execution or module loading.

Facts include a script entry body, functions, filters, lexical parent bodies, ordinary/advanced parameter declarations and type names, command occurrences and invocation forms, source spans, bounded snippets, module references, literal manifest metadata, and diagnostics. Comments, help text, and here-strings do not become executable command occurrences. Classes, enums, workflows, dynamic/deferred scriptblock semantics, DSC, and assembly loading remain unsupported or limited. Method-body semantic/type validation, data flow, alias environment reconstruction, and Pester shields are outside this worker.

File identities hash project-relative path bytes within the explicit project. Definition identities hash the owner identity, function/filter kind, case-folded name, and deterministic duplicate ordinal. They preserve path case and do not use line position as identity. Moving declarations between lines preserves IDs; renaming, changing lexical scope/path/kind, or reordering indistinguishable duplicates may change them. Occurrence IDs use deterministic file ordinals. Source spans use UTF-16 offsets/lengths and one-based lines/columns with exclusive end positions.

`PowerShellGraphMapper` produces `File` nodes for source files and `Method` nodes for script/function/filter bodies with PowerShell kind/owner metadata. A script file is never represented as a class or namespace. Only resolved callable occurrences become inferred `Calls`; dot-sourcing and module relationships use `DependsOn` with their distinct invocation metadata. Contains edges preserve lexical ownership, and edges without mapped endpoints are discarded. These persisted relationships do not authorize generic dependency queries. Only `resolve_exact_symbol(symbol, filePath, projectContext)` is admitted by the separate definition capability; project-wide PowerShell lookup remains unavailable.

## Conservative Binding Boundary

| Construct | Behavior |
|---|---|
| Unique established lexical function/filter, including ordinary recursion | Case-insensitive static binding within supported lexical visibility; inferred edge |
| Same-named declarations in unrelated files | Never global name candidates |
| Forward declarations, out-of-scope local definitions | `unresolved_local`, with no invented target |
| Conditional declarations, redefinitions, ambiguous imported names | `indeterminate` |
| Supported `$PSScriptRoot/relative.ps1` command path | Confined normalization against source directory and explicit selected files |
| Dot-source through that root expression | Distinct file dependency; only pure definition helpers expose supported names after the occurrence |
| Ordinary relative, absolute, qualified, computed, wildcard, backslash, or `Join-Path` expressions | Indeterminate; runtime working directory and arbitrary expression values are not assumed |
| Alias/function-provider mutation, indirect invocation, evaluated commands, unsupported scope modifiers, module-command shadowing, or script-root reassignment | Conservative binding limits; no fabricated local targets |
| No supported repository candidate | `external_unindexed`; no assertion about installed commands/runtime consumers |

The supported root expression is exactly an expandable string beginning `$PSScriptRoot/` with one root variable and a literal suffix, without additional interpolation or backticks. Escaping traversal is rejected without reading a target. A target outside the selected files is unverified, not proof of filesystem absence. Path comparison uses the request's explicit case policy. Static name inference is not a guarantee about the runtime alias environment, dynamic caller scopes, or arbitrary side effects of invoked code.

## Module And Manifest Boundary

Literal `Import-Module`, `using module`, and `#requires -Modules` facts retain their source identities and limitations. Requires facts preserve literal version/GUID constraints. Name-only, relative/search-path-dependent, version-qualified, computed, or unsupported import options remain unverified; no installed-module search or binary inspection runs.

A literal `Import-Module "$PSScriptRoot/tools.psm1"` can expose selected local functions only when the module has supported static initialization and export evidence. Initializers are limited to function definitions and supported `Export-ModuleMember -Function` statements. Exact literal names restrict visibility; empty exports legitimately expose nothing. Wildcard/computed/conditional exports, alias exports, nested dependencies, duplicate declarations, or executable module initialization prevent trusted static visibility. Import order can override an earlier local definition; a later unique local declaration can shadow an import. Multiple imported candidates remain ambiguous.

`.psd1` extraction accepts one bounded literal hashtable of scalar/array/nested-hashtable values. A supported literal `RootModule` resolves relative to the manifest directory within the selected inventory; `FunctionsToExport` further restricts the root module's exports. Required/nested modules, script hooks, required assemblies, alias/cmdlet exports, computed values, and wildcard visibility remain explicit limitations. Manifest expressions never run. These facts do not implement dependency installation, external module resolution, or module class imports.

## Bounds And Verification

Requests are bounded to 1 MiB and 1,000 files. Sources are bounded to 1 MiB per file, 16 MiB cumulative accepted bytes, 262,144 decoded characters per file, a conservative delimiter depth of 128, 1,000 per-file function/command/module facts plus one script body, 20,000 per-pass facts, and 128 parameters per body. Fact-budget failures discard a file's facts. The source reader may consume one bounded file that crosses the aggregate byte limit, then stops reading further files. Snippets are at most 2,000 characters per body and 512 per command. Diagnostics retain exact available native codes/messages/spans with up to 32 samples, 2,048 characters per message, and explicit total/truncation data. Literal tables/arrays have independent depth/collection/string bounds.

Cancellation is checked during reads, between files, around bounded synchronous native parsing, and during extraction/binding. Native synchronous parsing is not interruptible in its interior. Callers needing a hard wall-clock limit must bound the dedicated process lifetime; the packaged smoke harness does so. Failed parsing/decoding/budget checks never become successful complete query coverage.

```text
dotnet test tests/CodeMeridian.PowerShellIndexer.Tests
dotnet restore tools/PowerShellIndexer --locked-mode
dotnet publish tools/PowerShellIndexer -c Release -r win-x64 --self-contained true --no-restore -o .artifacts/powershell-worker/win-x64
dotnet publish tools/PowerShellIndexer -c Release -r linux-x64 --self-contained true --no-restore -o .artifacts/powershell-worker/linux-x64
python tools/PowerShellIndexer/tests/packaged_smoke.py <absolute-published-executable>
```

The worker package smoke uses fresh temporary synthetic repositories and child processes with empty `PATH` and nonexistent `DOTNET_ROOT`, so globally installed `pwsh` or .NET cannot supply its runtime. Windows and Linux x64 worker package smokes have been executed locally; the Linux run used WSL without installed `dotnet`/`pwsh`, and includes the symlink boundary fixture. A self-contained full Linux CLI also passed the publication/cache smoke under WSL, built from existing cached packages. Windows unit tests explicitly skip symlink creation when the required privilege is unavailable. The CI matrix declares installed-CLI and synthetic repository smokes on both platforms; no remote CI run is claimed. On October 29, NuGet-installed CLI/cache smokes and real server/database/MCP exact-definition acceptance passed on Windows and WSL Linux. Only explicit-file definitions are admitted; generic dependency/context/snippet capabilities remain unsupported. The shared E03/E04 rollout gates remain open.

## Opt-In CLI And Publication

Enable `indexing.powershell.enabled` in `meridian.json`. `--skip-powershell` on `index` or `profile` overrides configuration without hiding observed scripts. Local PowerShell options replace global options, including an explicit disabled setting. Paths with spaces and Unicode are supported. On hosts where uniform directory case semantics cannot be observed, explicitly configure `indexing.powershell.caseSensitive`; a setting that contradicts observed semantics fails. Observed case semantics are rechecked during analysis. This fallback is an explicit repository policy, not filesystem discovery evidence.

The CLI sends the complete accepted `.ps1`/`.psm1`/`.psd1` inventory to an isolated process on full, incremental, and watch passes. It rebinds unchanged consumers after source/import/export/deletion changes. Dry runs list selected files without running the worker or contacting publication. No worker build, restore, module lookup, or dependency installation runs automatically. Development fallback finds an already-built worker relative to the running CLI checkout, never in the analyzed repository. Parser dependencies are not loaded into the CLI or server.

Describe mode supplies implementation/parser/core-library fingerprints and the loaded .NET runtime version. Graph-mode output has a 16,777,216-character budget, stderr an 8,192-character budget, and the CLI terminates workers after two minutes or caller cancellation. Temporary request files live outside the analyzed repository and are removed after success/failure. Successful file hashes must match the captured inventory even if different bytes were only read transiently. Source/inventory or worker identity changes fail the pass.

Authenticated `POST /api/v1/knowledge/powershell` and SDK `PublishPowerShellGraphAsync` accept a bounded `PowerShellGraphSnapshot`: current generation, source and analyzer fingerprints, and all current PowerShell file outcomes. A transaction locks the same project profile state used for generation/outcome publication, checks exact inventory membership and identity collisions, replaces only PowerShell-owned edges and nodes, and records an immutable retry fingerprint. An exact retry is idempotent; different content or stale/reserved-later generations conflict. Failed files retain stale owned nodes with no retained dependency edges; removed nodes referenced by manual/other-producer edges remain tombstones. Manual edges, other languages and manual knowledge are preserved. Parsing happens before publication; transaction/transport failure cannot advance the CLI cache. Partial parsing/binding returns 2 and transport/protocol failure returns 1. Successful execution returns 0; complete definition coverage additionally requires successful full graph publication with verified actual read hashes and every file complete. Process exit alone cannot establish it. No PowerShell callable-dependency/class/namespace/data-flow/Pester capability is enabled.

Exact lookup checks canonical project/source identity, supported node kinds, analyzer/language ownership and complete publication status. Removed or stale retained nodes cannot appear as current definitions. The result lists IDs and source locations without exposing snippets. Missing, partial, delta, source-unbound or worker-mismatched outcomes remain unavailable, and generation changes discard results. A ready exact lookup never grants `get_context_for_editing`, `build_minimal_context`, `find_impact` or `find_connection` readiness.

## Bundling The Worker With The CLI

Publish both self-contained workers before packing the CLI:

```text
dotnet publish tools/PowerShellIndexer -c Release -r win-x64 --self-contained true --no-restore -o .artifacts/powershell-workers/win-x64
dotnet publish tools/PowerShellIndexer -c Release -r linux-x64 --self-contained true --no-restore -o .artifacts/powershell-workers/linux-x64
dotnet pack tools/Indexer -c Release
python tools/PowerShellIndexer/tests/packaged_cli_smoke.py <installed-codemeridian-executable>
```

`PowerShellWorkerArtifactsRoot` can override the staging root. Packing checks 18 required apphost, parser, managed runtime, and native runtime files across both RIDs. `python tools/Indexer/tests/worker_package_gate.py` verifies each missing-file case without building/restoring. Actual package smokes remain necessary to detect corrupt or incompatible files. The tool package carries separate RID folders and selects the executing platform's worker; Windows/Linux x64 are the tested worker platforms. NuGet extraction can remove Unix execute bits, so the launcher restores the user execute bit on its worker apphost when necessary; the installed worker must be writable for this repair. The main CLI requires its normal .NET 10 runtime, while each worker carries its own runtime/parser and needs no global PowerShell. The installed CLI smoke uses an authenticated local HTTP fixture to verify dispatch, source/generation binding, rebinding, deletions, partial outcomes, skip/dry-run behavior, and failed-run cache retention. That fixture does not establish Neo4j storage/query acceptance. See [acceptance commands and limits](../../docs/testing/project-capability-acceptance.md).
