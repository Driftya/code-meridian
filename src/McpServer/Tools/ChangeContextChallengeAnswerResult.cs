namespace CodeMeridian.McpServer.Tools;

public sealed record ChangeContextChallengeAnswerResult(
    string ContractVersion,
    string ChallengeId,
    bool IsCorrect,
    bool Halted,
    bool CanRetry,
    int Attempt,
    string State,
    IReadOnlyList<string> SelectedChoiceIds,
    IReadOnlyList<ChangeContextChallengeFeedback> Feedback);
