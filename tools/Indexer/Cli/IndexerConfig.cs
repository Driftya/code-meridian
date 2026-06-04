using System.Text.Json;

namespace CodeMeridian.Indexer.Cli;

internal sealed record IndexerConfig(string? Project, string? CodeMeridianUrl)
{
    public static IndexerConfig? Load(DirectoryInfo startDirectory)
    {
        var configFile = FindMeridianConfig(startDirectory);
        if (configFile is null)
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configFile.FullName));
            var root = document.RootElement;

            return new IndexerConfig(
                ReadString(root, "project"),
                ReadString(root, "codeMeridianUrl")
                ?? ReadString(root, "url"));
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

        var payload = new
        {
            project,
            codeMeridianUrl
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
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
}
