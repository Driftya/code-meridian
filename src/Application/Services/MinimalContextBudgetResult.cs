namespace CodeMeridian.Application.Services;

public sealed record MinimalContextBudgetResult(
    int RequestedTokens,
    int EstimatedTokens,
    int SourceSnippetBudgetTokens,
    int SourceSnippetEstimatedTokens,
    bool FitsRequestedBudget,
    int StructuredPayloadLimitBytes,
    string Complexity,
    string ModelGuidance,
    string ExpansionRisk,
    string Reason);
