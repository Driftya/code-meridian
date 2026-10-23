using CodeMeridian.Core.Projects;

namespace CodeMeridian.Core.RepositoryConstraints;

public sealed record StoredRepositoryConstraintSnapshot(
    long Generation,
    long LatestGeneration,
    ProjectProfile Profile,
    IReadOnlyList<ProjectInventoryFile> Files,
    RepositoryConstraintEvidence? Evidence);
