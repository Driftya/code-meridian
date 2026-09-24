namespace CodeMeridian.McpServer.Tools;

public sealed record ChangeContextChallengeEvidence(
    IReadOnlyList<string> Source,
    IReadOnlyList<string> Tests);
