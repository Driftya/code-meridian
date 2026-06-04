# Publishing the Indexer Tool

The command below only works after `CodeMeridian.Indexer` has been published to NuGet.org or to a NuGet feed configured on the user's machine:

```powershell
dotnet tool install --global CodeMeridian.Indexer
```

Until then, install from a locally built package with `--add-source` as shown in [Installation](installation.md).

## Package Project

The .NET tool package is produced by:

```text
tools/Indexer/CodeMeridian.Indexer.csproj
```

Important package metadata:

```xml
<PackAsTool>true</PackAsTool>
<ToolCommandName>codemeridian</ToolCommandName>
<PackageId>CodeMeridian.Indexer</PackageId>
<Version>1.0.0</Version>
```

The installed command is:

```powershell
codemeridian
```

## Build and Test Before Publishing

Run:

```powershell
dotnet build CodeMeridian.sln
dotnet test --no-build
dotnet pack tools/Indexer/CodeMeridian.Indexer.csproj -c Release -o artifacts/packages
```

Smoke-test the package locally:

```powershell
$toolPath = Join-Path $PWD "artifacts/tooltest"
if (Test-Path $toolPath) { Remove-Item -LiteralPath $toolPath -Recurse -Force }

dotnet tool install CodeMeridian.Indexer `
  --version 1.0.0 `
  --tool-path $toolPath `
  --add-source artifacts/packages

& (Join-Path $toolPath "codemeridian.exe") --list-capabilities
& (Join-Path $toolPath "codemeridian.exe") index . --dry-run
```

## Publish to NuGet.org

Create a NuGet API key from your NuGet.org account, then push the package:

```powershell
dotnet nuget push artifacts/packages/CodeMeridian.Indexer.1.0.0.nupkg `
  --api-key $env:NUGET_API_KEY `
  --source https://api.nuget.org/v3/index.json
```

After NuGet finishes indexing the package, users can install it with:

```powershell
dotnet tool install --global CodeMeridian.Indexer
```

## GitHub Actions Publishing

The repository includes a workflow:

```text
.github/workflows/publish-indexer.yml
```

It builds, tests, packs, smoke-tests, uploads the package artifact, and can publish to NuGet.org.

### Required Secret

Add this repository secret in GitHub:

```text
NUGET_API_KEY
```

Use a NuGet.org API key scoped to push `CodeMeridian.Indexer`.

### Publish From a Tag

Create and push a SemVer tag. The workflow strips the leading `v` and uses the rest as the package version.

```powershell
git tag v1.0.1
git push origin v1.0.1
```

This publishes:

```text
CodeMeridian.Indexer 1.0.1
```

Preview tags are supported:

```powershell
git tag v1.1.0-preview.1
git push origin v1.1.0-preview.1
```

### Manual Workflow Run

You can also run **Publish Indexer Tool** manually from GitHub Actions.

Inputs:

- `version`: package version to build.
- `publish`: when `false`, the workflow only builds, tests, packs, smoke-tests, and uploads the `.nupkg` artifact. When `true`, it also pushes to NuGet.org.

Use manual runs for package validation before publishing.

## Publish to a Private Feed

Push to your feed:

```powershell
dotnet nuget push artifacts/packages/CodeMeridian.Indexer.1.0.0.nupkg `
  --api-key $env:NUGET_API_KEY `
  --source https://your-feed.example/v3/index.json
```

Users install with:

```powershell
dotnet tool install --global CodeMeridian.Indexer `
  --add-source https://your-feed.example/v3/index.json
```

## Versioning

NuGet packages are immutable. Before publishing a new package, update:

```xml
<Version>...</Version>
```

in `tools/Indexer/CodeMeridian.Indexer.csproj`.

Recommended pattern while the project is early:

- Patch releases for fixes: `1.0.1`, `1.0.2`
- Minor releases for new indexer/tool features: `1.1.0`
- Previews for unstable packages: `1.1.0-preview.1`

Install a preview explicitly:

```powershell
dotnet tool install --global CodeMeridian.Indexer --version 1.1.0-preview.1
```

## Updating an Installed Tool

From NuGet.org:

```powershell
dotnet tool update --global CodeMeridian.Indexer
```

From a local package folder:

```powershell
dotnet tool update CodeMeridian.Indexer --global --add-source artifacts/packages
```

## Common Publishing Checks

- The package should include the README.
- `codemeridian --list-capabilities` should run after install.
- `codemeridian index . --dry-run` should work from outside the repository.
- TypeScript indexing should restore npm dependencies on first use when needed.
- Do not publish generated `artifacts/` output to the repository.
