using System.Text.Json;

namespace CodeMeridian.Indexer.Cli;

internal sealed record IndexerConfig(string? Project, string? CodeMeridianUrl, bool AllowRepoScripts)
{
    private const string MeridianSampleFileName = "meridian.sample.json";
    private const string ConfigFileName = "meridian.json";

    public static IndexerConfig? Load(DirectoryInfo startDirectory, DirectoryInfo? globalConfigDirectory = null)
    {
        var localConfigFile = FindLocalMeridianConfig(startDirectory);
        if (localConfigFile is not null)
            return LoadFile(localConfigFile, ignoreProject: false);

        var globalConfigFile = FindGlobalMeridianConfig(globalConfigDirectory);
        return globalConfigFile is null ? null : LoadFile(globalConfigFile, ignoreProject: true);
    }

    public static IndexerConfig? LoadLocal(DirectoryInfo startDirectory)
    {
        var configFile = FindLocalMeridianConfig(startDirectory);
        return configFile is null ? null : LoadFile(configFile, ignoreProject: false);
    }

    public static DirectoryInfo GetGlobalConfigDirectory()
    {
        var overridePath = Environment.GetEnvironmentVariable("CODEMERIDIAN_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(overridePath))
            return new DirectoryInfo(overridePath);

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
            return new DirectoryInfo(Path.Combine(appData, "CodeMeridian"));

        return new DirectoryInfo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "codemeridian"));
    }

    public static FileInfo GetGlobalConfigFile(DirectoryInfo? globalConfigDirectory = null) =>
        new(Path.Combine((globalConfigDirectory ?? GetGlobalConfigDirectory()).FullName, ConfigFileName));

    public static void WriteGlobal(string codeMeridianUrl, bool overwrite = false, DirectoryInfo? globalConfigDirectory = null)
    {
        Write(globalConfigDirectory ?? GetGlobalConfigDirectory(), project: null, codeMeridianUrl, overwrite);
    }

    private static IndexerConfig? LoadFile(FileInfo configFile, bool ignoreProject)
    {
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
                ignoreProject ? null : ReadString(root, "project"),
                ReadString(root, "codeMeridianUrl")
                ?? ReadString(root, "url"),
                ReadBoolean(root, "allowRepoScripts") ?? false);
        }
        catch
        {
            return null;
        }
    }

    public static void Write(DirectoryInfo rootDirectory, string? project, string codeMeridianUrl, bool overwrite = false)
    {
        Directory.CreateDirectory(rootDirectory.FullName);

        var filePath = Path.Combine(rootDirectory.FullName, ConfigFileName);
        if (File.Exists(filePath) && !overwrite)
            throw new InvalidOperationException($"Config file already exists: {filePath}. Use --force to overwrite it.");

        var json = BuildMeridianJson(project, codeMeridianUrl);
        File.WriteAllText(filePath, json + Environment.NewLine);
        WriteSchemaFile(rootDirectory, overwrite);
    }

    private static string BuildMeridianJson(string? project, string codeMeridianUrl)
    {
        var template = ReadRequiredTemplate(MeridianSampleFileName);
        return template
            .Replace("{{project}}", JsonEncodedText.Encode(project ?? string.Empty).ToString(), StringComparison.Ordinal)
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

    private static FileInfo? FindLocalMeridianConfig(DirectoryInfo directory)
    {
        for (var current = directory; current is not null; current = current.Parent)
        {
            var configFile = new FileInfo(Path.Combine(current.FullName, ConfigFileName));
            if (configFile.Exists)
                return configFile;
        }

        return null;
    }

    private static FileInfo? FindGlobalMeridianConfig(DirectoryInfo? globalConfigDirectory)
    {
        var configFile = GetGlobalConfigFile(globalConfigDirectory);
        return configFile.Exists ? configFile : null;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        var result = value.GetString();
        return string.IsNullOrWhiteSpace(result) ? null : result;
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
