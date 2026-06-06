# Package the Indexers for Easier Use

- Status: done
- Priority: P1
- Note: The old language-specific indexer commands worked for contributors, but they were not a polished user experience


**Why:** The old language-specific indexer commands worked for contributors, but they were not a polished user experience. Users should not need to know whether to run `tools/RoslynIndexer`, `tools/TsIndexer`, or another future language indexer; they should install one thing and run one command against a repo.

**Best target experience:**

```powershell
codemeridian index .
codemeridian index C:\Projects\MyApp --project MyApp --watch
codemeridian index . --clear
```

**Packaging options:**

- Publish `tools/Indexer` as a .NET global tool: `dotnet tool install -g CodeMeridian.Indexer`.
- Publish the TypeScript indexer as an npm package for JS-only environments.
- Keep `tools/Indexer` as the recommended unified CLI that dispatches to C#, TypeScript, docs, diagnostics, and future indexers.
- Add a Docker-based indexing option for CI or machines without local SDK setup.

**CLI improvements:**

- One command for C#, TypeScript, docs, diagnostics, and future HTML/CSS indexing.
- Auto-detect project type, package manager, solution file, `tsconfig.json`, ESLint config, and repo root.
- Clear installation docs for local, CI, and Docker usage.
- Stable exit codes for CI.
- `--dry-run` to show what will be indexed.
- `--list-capabilities` to show which indexers are available on the current machine.
- `--skip-csharp`, `--skip-typescript`, `--skip-docs`, `--skip-diagnostics` flags.

**Repository impact:**

- Keep language-specific indexers internally modular.
- Move shared CLI parsing/config/env loading into a common library if duplication grows.
- Ensure auth/server URL handling is consistent across all indexers.

**Effort:** Medium  
**Value:** High  
**Risk:** Medium, mostly around packaging, versioning, and cross-platform command execution.

