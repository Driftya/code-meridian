namespace CodeMeridian.Sdk.Models;

public sealed record AgentResponse
{
    public required string Content { get; init; }
    public string? AgentName { get; init; }
    public IReadOnlyList<string> Sources { get; init; } = [];
    public Dictionary<string, object?> Metadata { get; init; } = [];
    public bool IsSuccess { get; init; } = true;
    public string? ErrorMessage { get; init; }
}
