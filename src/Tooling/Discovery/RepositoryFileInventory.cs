namespace CodeMeridian.Tooling.Discovery;

public sealed record RepositoryFileInventory(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Warnings,
    int WarningCount)
{
    public bool IsComplete => WarningCount == 0;
}
