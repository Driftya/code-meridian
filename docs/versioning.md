# Versioning

CodeMeridian has a few different version numbers. They do not all mean the same thing, and they should not all be bumped together.

## Version Types

### Product version

This is the normal release version for a shipped component such as:

- `CodeMeridian.Indexer`
- `CodeMeridian.McpServer`
- `CodeMeridian.Sdk`

It is the SemVer-style version users see in packages, tool output, release tags, and server version endpoints.

Bump this when:

- you publish a normal release
- you publish a preview build
- you want users to be able to distinguish one shipped build from another

This version can change without changing graph compatibility.

### Graph contract version

This is the compatibility version for the stored code graph shape and expectations.

It answers this question:

> Can this indexer and this MCP server safely read and write the same graph model?

Bump this only when graph compatibility changes in a breaking way, for example:

- node IDs change in a way that old readers cannot trust
- relationship meaning changes
- required node or edge properties change
- server queries start depending on graph data that older indexers do not write

Do not bump this for:

- bug fixes that preserve the same graph contract
- ordinary package releases
- internal refactors

### Cache version

This is the compatibility version for local cache formats such as `.meridian/cache`.

It answers this question:

> Can this build safely reuse cache data written by an older build?

Bump this when:

- cache file JSON shape changes
- cache keys or hash rules change
- cache semantics change enough that reuse would be unsafe

Do not bump this for:

- server-only changes
- graph-only changes that do not affect local cache format
- normal releases with unchanged cache structure

## Source Of Truth

The shared version source lives in `Directory.Build.props`.

That file defines:

- `VersionPrefix`
- `VersionSuffix`
- `Version`
- `PackageVersion`
- `AssemblyVersion`
- `FileVersion`
- `InformationalVersion`
- `CodeMeridianGraphContractVersion`
- `CodeMeridianCacheVersion`

Projects should inherit these values instead of hardcoding their own package versions unless a project truly needs an override.

## How Versions Flow

The intended flow is:

1. A release tag such as `v1.2.3` or `v1.2.3-preview.1`, or a manual workflow input, provides the product version.
2. CI passes that resolved version into MSBuild with `/p:Version=...`.
3. The CLI reads its own assembly metadata for local version output.
4. The MCP Docker build forwards the same version into `dotnet publish`, so the server assembly metadata matches the image tag.
5. The MCP server exposes its assembly metadata through `/api/v1/status/version`.

## Release Workflow Rules

### Product releases

- `publish-indexer.yml` publishes the indexer package from version tags.
- `publish-mcp.yml` publishes the MCP image from the same version tags.
- Use the same SemVer for both when they are part of the same release.
- Stable tags look like `v1.2.3`.
- Preview tags look like `v1.2.3-preview.1`.

### Manual publishing

- Both publish workflows also support manual dispatch with an explicit version input.
- Manual runs should use a real SemVer version, not floating labels like `preview` or `latest`.
- Manual runs are for exceptional cases. Normal releases should be tag-driven so the indexer package and MCP image stay aligned.

### MCP image tags

- A stable tag publish also emits `latest`.
- A preview tag publish does not emit `latest`.
- The versioned image tag and the server's reported assembly version should match.

## Bump Rules

### Bump only the product version

Use this for most releases.

Examples:

- CLI UX improvement
- bug fix in config parsing
- faster graph query with the same result shape

### Bump the graph contract version too

Use this only when older indexers and newer servers, or newer indexers and older servers, should not be treated as graph-compatible.

Examples:

- a new required edge property is introduced
- a node type meaning changes
- an indexer stops writing data that the server assumes exists

### Bump the cache version too

Use this only when local cache reuse would be incorrect.

Examples:

- file snapshot format changes
- cached selection logic changes in a way that makes old entries unsafe

## Current CLI Behavior

`codemeridian version` prints:

- local client tool version
- local graph contract version
- local cache version
- MCP server version details when the server responds

If the MCP version lookup fails, the CLI still prints the local tool version and reports that the MCP version fetch failed.
