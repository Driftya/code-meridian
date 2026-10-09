namespace CodeMeridian.Core.Projects;

public sealed record StoredProjectProfile(
    long Generation,
    long LatestGeneration,
    DateTimeOffset PublishedAt,
    ProjectProfile Profile,
    ProjectInventoryFile? Target);
