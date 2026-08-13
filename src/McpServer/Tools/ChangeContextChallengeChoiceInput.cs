using System.ComponentModel;

namespace CodeMeridian.McpServer.Tools;

public sealed record ChangeContextChallengeChoiceInput(
    [property: Description("Stable short choice ID, such as A, B, C, or D.")]
    string Id,
    [property: Description("One plausible code answer. Keep it bounded and directly relevant to the question.")]
    string Code,
    [property: Description("Whether this choice is correct. Exactly one or two choices may be correct.")]
    bool IsCorrect,
    [property: Description("Concise teaching feedback explaining why this choice is right or wrong.")]
    string Feedback);
