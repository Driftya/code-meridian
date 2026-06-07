using System.Text.Json;

namespace CodeMeridian.Indexer.Cli;

internal sealed record IndexerConfig(string? Project, string? CodeMeridianUrl, bool AllowRepoScripts)
{
    private const string MeridianSampleFileName = "meridian.sample.json";

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

        var json = BuildMeridianJson(project, codeMeridianUrl);
        File.WriteAllText(filePath, json + Environment.NewLine);
        WriteSchemaFile(rootDirectory, overwrite);
    }

    private static string BuildMeridianJson(string project, string codeMeridianUrl)
    {
        var template = ReadRequiredTemplate(MeridianSampleFileName);
        return template
            .Replace("{{project}}", JsonEncodedText.Encode(project).ToString(), StringComparison.Ordinal)
            .Replace("{{codeMeridianUrl}}", JsonEncodedText.Encode(codeMeridianUrl).ToString(), StringComparison.Ordinal)
            .TrimEnd();
    }

    private static void WriteSchemaFile(DirectoryInfo rootDirectory, bool overwrite)
    {
        var targetPath = Path.Combine(rootDirectory.FullName, "meridian.schema.json");
        if (File.Exists(targetPath) && !overwrite)
            return;

        var sourcePath = Path.Combine(AppContext.BaseDirectory, "meridian.schema.json");
        if (File.Exists(sourcePath))
        {
            File.Copy(sourcePath, targetPath, overwrite: true);
        }
    }

    private static string ReadRequiredTemplate(string fileName)
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(sourcePath))
            return File.ReadAllText(sourcePath);

        throw new InvalidOperationException($"Required template file is missing: {sourcePath}");
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
