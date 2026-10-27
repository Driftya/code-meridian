# Offline PowerShell Worker

Status: D01–D06 of the [capability checklist](../../docs/plans/2026-10-19-project-capability-completion-checklist.md) provide an isolated parser, static facts, conservative bindings, and graph mapping. The main indexer does not dispatch or publish this worker yet. PowerShell remains detected but unsupported in production profiles and semantic query routing. D07–D10 cover publication, incremental rebinding, opt-in configuration, query conformance, and packaged main-CLI delivery.

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

`PowerShellGraphMapper` produces `File` nodes for source files and `Method` nodes for script/function/filter bodies with PowerShell kind/owner metadata. A script file is never represented as a class or namespace. Only resolved callable occurrences become inferred `Calls`; dot-sourcing and module relationships use `DependsOn` with their distinct invocation metadata. Contains edges preserve lexical ownership, and edges without mapped endpoints are discarded. These are offline mappings, not publication or proof that every existing generic query is compatible; D09 must audit those queries before activation.

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

The package smoke uses fresh temporary synthetic repositories and child processes with empty `PATH` and nonexistent `DOTNET_ROOT`, so globally installed `pwsh` or .NET cannot supply the runtime. Windows and Linux x64 package smokes have been executed locally; the Linux run used WSL without installed `dotnet`/`pwsh`, and includes the symlink boundary fixture. Windows unit tests explicitly skip symlink creation when the required privilege is unavailable; Linux packaging verifies that boundary. The dedicated CI matrix also declares Windows/Linux unit and self-contained smoke checks; this session does not claim a remote CI run. Main-CLI packaging, live graph acceptance, and release activation remain open.
