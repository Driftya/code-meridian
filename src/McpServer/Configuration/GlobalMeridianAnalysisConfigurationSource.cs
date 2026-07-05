using System.Text.Json;
using CodeMeridian.Application.Services;
using CodeMeridian.Tooling.Configuration;

namespace CodeMeridian.McpServer.Configuration;

public sealed class GlobalMeridianAnalysisConfigurationSource(
    CodeMeridianConfigFileStore configFileStore,
    ILogger<GlobalMeridianAnalysisConfigurationSource> logger) : IGlobalAnalysisConfigurationSource
{
    public ValueTask<AnalysisConfigurationSourceResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        var configFile = configFileStore.GetGlobalConfigFile();
        if (!configFile.Exists)
            return ValueTask.FromResult(new AnalysisConfigurationSourceResult([], [], $"global `{configFile.FullName}`"));

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(configFile.FullName),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

            if (!document.RootElement.TryGetProperty("analysis", out var analysisElement))
                return ValueTask.FromResult(new AnalysisConfigurationSourceResult([], [], $"global `{configFile.FullName}`"));

            if (analysisElement.ValueKind is not JsonValueKind.Object and not JsonValueKind.Array)
            {
                return ValueTask.FromResult(new AnalysisConfigurationSourceResult(
                    [],
                    [$"Ignored global `analysis` in `{configFile.FullName}` because it is not a JSON object or array."],
                    $"global `{configFile.FullName}`"));
            }

            var entries = new List<AnalysisConfigurationEntry>();
            Flatten("analysis", analysisElement, entries);
            return ValueTask.FromResult(new AnalysisConfigurationSourceResult(entries, [], $"global `{configFile.FullName}`"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Failed to load global analysis configuration from {Path}", configFile.FullName);
            return ValueTask.FromResult(new AnalysisConfigurationSourceResult(
                [],
                [$"Failed to load global `meridian.json` analysis from `{configFile.FullName}`: {ex.Message}"],
                $"global `{configFile.FullName}`"));
        }
    }

    private static void Flatten(
        string path,
        JsonElement element,
        ICollection<AnalysisConfigurationEntry> entries)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Flatten($"{path}:{property.Name}", property.Value, entries);
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Flatten($"{path}:{index}", item, entries);
                    index++;
                }
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                break;

            default:
                entries.Add(new AnalysisConfigurationEntry(path, ReadScalarValue(element)));
                break;
        }
    }

    private static string? ReadScalarValue(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => element.GetRawText(),
            _ => element.GetRawText()
        };
}
