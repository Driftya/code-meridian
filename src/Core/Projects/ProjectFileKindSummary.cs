namespace CodeMeridian.Core.Projects;

public sealed record ProjectFileKindSummary(
    string Kind,
    int Count,
    IReadOnlyList<string> ExamplePaths);
