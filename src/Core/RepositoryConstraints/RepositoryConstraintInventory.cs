using CodeMeridian.Core.Projects;

namespace CodeMeridian.Core.RepositoryConstraints;

public sealed record RepositoryConstraintInventory(
    long Generation,
    string DiscoveryState,
    bool CaseSensitive,
    IReadOnlyList<ProjectInventoryFile> Files,
    IReadOnlyList<string> ExcludedPaths);
