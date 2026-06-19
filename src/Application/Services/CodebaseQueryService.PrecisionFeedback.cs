using System.Text.Json;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private PrecisionFeedbackSnapshot? LoadPrecisionFeedback()
    {
        if (!analysisOptions.PrecisionFeedback.Enabled)
            return null;

        var configuredPath = analysisOptions.PrecisionFeedback.FeedbackFilePath;
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;

        var fullPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(Directory.GetCurrentDirectory(), configuredPath);
        if (!File.Exists(fullPath))
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
            var root = document.RootElement;
            var tools = new List<ToolPrecisionSnapshot>();

            if (root.TryGetProperty("tools", out var toolsElement) && toolsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var toolElement in toolsElement.EnumerateArray())
                {
                    tools.Add(new ToolPrecisionSnapshot(
                        GetString(toolElement, "toolName") ?? string.Empty,
                        GetInt(toolElement, "suggestedFileCount"),
                        GetInt(toolElement, "acceptedFileCount"),
                        GetInt(toolElement, "ignoredFileCount"),
                        GetInt(toolElement, "suggestedTestCount"),
                        GetInt(toolElement, "acceptedTestCount"),
                        GetInt(toolElement, "ignoredTestCount"),
                        GetInt(toolElement, "exactTargets"),
                        GetInt(toolElement, "fileOnlyTargets"),
                        GetInt(toolElement, "heuristicTargets"),
                        GetInt(toolElement, "staleTargets"),
                        GetInt(toolElement, "staleWarnings"),
                        GetInt(toolElement, "manualFallbackCommands"),
                        GetPaths(toolElement, "files"),
                        GetPaths(toolElement, "tests")));
                }
            }

            return new PrecisionFeedbackSnapshot(tools);
        }
        catch
        {
            return null;
        }
    }

    private ToolPrecisionSnapshot? FindToolPrecisionFeedback(string toolName)
    {
        var tools = LoadPrecisionFeedback()?.Tools;
        if (tools is null)
            return null;

        return tools.FirstOrDefault(tool =>
            string.Equals(tool.ToolName, toolName, StringComparison.OrdinalIgnoreCase)
            || tool.ToolName.EndsWith(toolName, StringComparison.OrdinalIgnoreCase));
    }

    private SurfaceFeedback EvaluateSurfaceFeedback(string toolName, string filePath)
    {
        var tool = FindToolPrecisionFeedback(toolName);
        if (tool is null)
            return SurfaceFeedback.None;

        var file = tool.Files.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, filePath, StringComparison.OrdinalIgnoreCase));

        var scoreAdjustment = 0;
        var reasons = new List<string>();

        if (file is not null)
        {
            if (file.AcceptedCount > 0)
            {
                scoreAdjustment += analysisOptions.PrecisionFeedback.AcceptedFileBoost * file.AcceptedCount;
                reasons.Add($"feedback accepted {file.AcceptedCount}/{file.SuggestedCount} prior sessions");
            }

            if (file.IgnoredCount > 0)
            {
                scoreAdjustment -= analysisOptions.PrecisionFeedback.IgnoredFilePenalty * file.IgnoredCount;
                reasons.Add($"feedback ignored {file.IgnoredCount}/{file.SuggestedCount} prior sessions");
            }
        }

        if (tool.FileOnlyTargets > 0)
            reasons.Add($"historical file-only pressure: {tool.FileOnlyTargets}");
        if (tool.HeuristicTargets > 0)
            reasons.Add($"historical heuristic pressure: {tool.HeuristicTargets}");
        if (tool.StaleTargets > 0 || tool.StaleWarnings > 0)
            reasons.Add($"historical stale pressure: {tool.StaleTargets + tool.StaleWarnings}");

        return reasons.Count == 0
            ? SurfaceFeedback.None
            : new SurfaceFeedback(scoreAdjustment, string.Join(", ", reasons), tool);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number)
            ? number
            : 0;

    private static IReadOnlyList<PathPrecisionSnapshot> GetPaths(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var pathsElement) || pathsElement.ValueKind != JsonValueKind.Array)
            return [];

        var paths = new List<PathPrecisionSnapshot>();
        foreach (var pathElement in pathsElement.EnumerateArray())
        {
            paths.Add(new PathPrecisionSnapshot(
                GetString(pathElement, "path") ?? string.Empty,
                GetInt(pathElement, "suggestedCount"),
                GetInt(pathElement, "acceptedCount"),
                GetInt(pathElement, "ignoredCount")));
        }

        return paths;
    }

    private sealed record SurfaceFeedback(int ScoreAdjustment, string Reason, ToolPrecisionSnapshot? Tool)
    {
        public static SurfaceFeedback None { get; } = new(0, string.Empty, null);
    }
}

internal sealed record PrecisionFeedbackSnapshot(
    IReadOnlyList<ToolPrecisionSnapshot> Tools);

internal sealed record ToolPrecisionSnapshot(
    string ToolName,
    int SuggestedFileCount,
    int AcceptedFileCount,
    int IgnoredFileCount,
    int SuggestedTestCount,
    int AcceptedTestCount,
    int IgnoredTestCount,
    int ExactTargets,
    int FileOnlyTargets,
    int HeuristicTargets,
    int StaleTargets,
    int StaleWarnings,
    int ManualFallbackCommands,
    IReadOnlyList<PathPrecisionSnapshot> Files,
    IReadOnlyList<PathPrecisionSnapshot> Tests);

internal sealed record PathPrecisionSnapshot(
    string Path,
    int SuggestedCount,
    int AcceptedCount,
    int IgnoredCount);
