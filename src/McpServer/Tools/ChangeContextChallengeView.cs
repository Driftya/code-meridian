namespace CodeMeridian.McpServer.Tools;

public sealed record ChangeContextChallengeView(
    string ContractVersion,
    string ChallengeId,
    string NodeId,
    string Question,
    ChangeContextChallengeEvidence Evidence,
    int RequiredSelectionCount,
    IReadOnlyList<ChangeContextChallengeChoiceView> Choices,
    int Attempt,
    string State,
    DateTimeOffset ExpiresAt,
    string TrustNotice);
