namespace CodeMeridian.Application.Services;

public sealed record TestRecommendationResult(
    GraphNodeResult TestNode,
    string Category,
    string Reason,
    int Score);
