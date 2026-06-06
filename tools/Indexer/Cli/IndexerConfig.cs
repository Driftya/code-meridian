using System.Text.Json;

namespace CodeMeridian.Indexer.Cli;

internal sealed record IndexerConfig(string? Project, string? CodeMeridianUrl, bool AllowRepoScripts)
{
    public static IndexerConfig? Load(DirectoryInfo startDirectory)
    {
        var configFile = FindMeridianConfig(startDirectory);
        if (configFile is null)
            return null;

        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(configFile.FullName),
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
            var root = document.RootElement;

            return new IndexerConfig(
                ReadString(root, "project"),
                ReadString(root, "codeMeridianUrl")
                ?? ReadString(root, "url"),
                ReadBoolean(root, "allowRepoScripts") ?? false);
        }
        catch
        {
            return null;
        }
    }

    public static void Write(DirectoryInfo rootDirectory, string project, string codeMeridianUrl, bool overwrite = false)
    {
        Directory.CreateDirectory(rootDirectory.FullName);

        var filePath = Path.Combine(rootDirectory.FullName, "meridian.json");
        if (File.Exists(filePath) && !overwrite)
            throw new InvalidOperationException($"Config file already exists: {filePath}. Use --force to overwrite it.");

        var json = $$"""
            {
              "project": "{{project}}",
              "codeMeridianUrl": "{{codeMeridianUrl}}",
              // Enabled by default so repo-controlled build and lint diagnostics can run on trusted repos.
              "allowRepoScripts": true
            }
            """;

        File.WriteAllText(filePath, json + Environment.NewLine);
    }

    private static FileInfo? FindMeridianConfig(DirectoryInfo directory)
    {
        for (var current = directory; current is not null; current = current.Parent)
        {
            var configFile = new FileInfo(Path.Combine(current.FullName, "meridian.json"));
            if (configFile.Exists)
                return configFile;
        }

        return null;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? ReadBoolean(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
