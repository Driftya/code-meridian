using System.Text.Json;

namespace CodeMeridian.Sdk;

public sealed record ProjectProfileResponse(
    string Status,
    string Project,
    long? Generation,
    long? LatestGeneration,
    DateTimeOffset? PublishedAt,
    JsonElement? Profile,
    ProjectProfileTargetResponse? Target,
    IReadOnlyList<string> Warnings,
    string ContractVersion);
