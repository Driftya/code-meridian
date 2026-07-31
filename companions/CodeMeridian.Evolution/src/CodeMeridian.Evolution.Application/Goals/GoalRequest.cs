namespace CodeMeridian.Evolution.Application.Goals;

public sealed record GoalRequest(
    Guid Id,
    string Title,
    string Actor,
    string SuccessCriteria,
    DateTimeOffset? Deadline,
    decimal Budget,
    string IdempotencyKey);
