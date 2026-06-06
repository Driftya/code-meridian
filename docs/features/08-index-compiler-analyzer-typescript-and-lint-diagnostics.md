# Index Compiler, Analyzer, TypeScript, and Lint Diagnostics

- Status: done
- Priority: P1
- Note: Build errors, compiler warnings, analyzer findings, TypeScript diagnostics, and lint warnings are some of the highest-signal context an AI coding tool can receive


**Why:** Build errors, compiler warnings, analyzer findings, TypeScript diagnostics, and lint warnings are some of the highest-signal context an AI coding tool can receive. They tell the assistant what is already broken, what code style rules matter in the project, and which files need attention before a change is safe.

**Outcome:** CodeMeridian can answer questions like:

- What warnings exist near this method?
- Which changed files currently fail lint or type checking?
- What diagnostics should Copilot fix first?
- Does this refactor introduce new compiler or ESLint warnings?

**C# sources to support:**

- `dotnet build` diagnostics from MSBuild output.
- Roslyn compiler warnings and errors.
- Analyzer diagnostics from the project's existing `.editorconfig`, `Directory.Build.props`, package analyzers, and rulesets.
- Optional `dotnet format --verify-no-changes` style diagnostics later.

**TypeScript / JavaScript sources to support:**

- `tsc --noEmit` diagnostics using the project's own `tsconfig.json`.
- ESLint diagnostics using the project's own config when present.
- Prefer package scripts first, such as `npm run lint`, `pnpm lint`, or `yarn lint`, because projects often wrap ESLint with the correct flags.
- Fall back to local binaries: `node_modules/.bin/eslint` and `node_modules/.bin/tsc`.

**Graph model:**

- Add a `Diagnostic` node type or equivalent external concept type.
- Store severity, code/rule ID, message, file, line, column, source tool, and project context.
- Link diagnostics to the nearest file node and, when possible, nearest class/method node by line range.
- Track timestamps so fixed diagnostics disappear after re-indexing or can be shown as recently resolved.

**Suggested tools:**

- `find_diagnostics`
- `find_diagnostics_for_node`
- `find_diagnostics_for_project`
- Include diagnostics in `build_minimal_context` by default in compact form.

**Important config behavior:**

- Do not invent lint rules. Use the target project's existing config.
- Detect package manager lockfiles and scripts before choosing commands.
- Allow diagnostics indexing to be disabled or run separately because lint/build commands can be slow or require dependencies.
- Mark unavailable diagnostics clearly when dependencies are missing or scripts fail.

**Effort:** Medium to high  
**Value:** High  
**Risk:** Medium, because command execution differs across repositories and ESLint config discovery can be messy.

**Implemented first slice:** Diagnostics are indexed as `Diagnostic` code nodes by default, using `dotnet build --no-restore --nologo`, local `tsc --noEmit --pretty false`, and project lint scripts or local ESLint when available. Use `--skip-diagnostics` for faster structural-only indexing. Query tools `find_diagnostics` and `find_diagnostics_for_node` expose the results.

