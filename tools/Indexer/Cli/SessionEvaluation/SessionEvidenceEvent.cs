using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeMeridian.Indexer.Cli.SessionEvaluation;

internal sealed class SessionEvidenceEvent
{
    public DateTimeOffset? Timestamp { get; init; }

    public string? Provider { get; init; }

    public string? Project { get; init; }

    public string? Kind { get; init; }

    public string? ToolName { get; init; }

    public string? Command { get; init; }

    public string? TargetConfidence { get; init; }

    public bool? StaleWarning { get; init; }

    public string? ContextPackStatus { get; init; }

    public IReadOnlyList<string> Files { get; init; } = [];

    public IReadOnlyList<string> Tests { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
