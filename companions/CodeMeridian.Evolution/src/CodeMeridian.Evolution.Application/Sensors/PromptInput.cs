namespace CodeMeridian.Evolution.Application.Sensors;

public sealed record PromptInput(
    string Text,
    string Actor,
    string ProjectId,
    string IdempotencyKey);
