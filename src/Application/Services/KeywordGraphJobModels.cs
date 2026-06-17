namespace CodeMeridian.Application.Services;

public sealed record KeywordGraphJobStatus(
    Guid JobId,
    string Operation,
    string? ProjectContext,
    string State,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CompletedAt,
    string? Summary,
    string? Error);

public sealed record KeywordGraphJobSubmissionResult(
    bool Accepted,
    string Message,
    KeywordGraphJobStatus Job);
