namespace CodeMeridian.Application.Services;

public sealed record ConnectionNodeResult(
    int Order,
    string Id,
    string Name,
    string Type,
    string? Namespace,
    string? FilePath,
    int? LineNumber,
    string? ProjectContext);
