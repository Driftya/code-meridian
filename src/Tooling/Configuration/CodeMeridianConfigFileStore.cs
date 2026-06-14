using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace CodeMeridian.Tooling.Configuration;

public sealed class CodeMeridianConfigFileStore
{
    public const int CurrentConfigVersion = 1;

    private const string MeridianSampleFileName = "meridian.sample.json";
    private const string ConfigFileName = "meridian.json";

    public CodeMeridianConfigSnapshot? LoadLocal(DirectoryInfo startDirectory)
    {
        var configFile = FindLocalConfig(startDirectory);
        return configFile is null ? null : LoadFile(configFile, ignoreProject: false);
    }

    public CodeMeridianConfigSnapshot? LoadGlobal(DirectoryInfo? globalConfigDirectory = null)
    {
        var configFile = FindGlobalConfig(globalConfigDirectory);
        return configFile is null ? null : LoadFile(configFile, ignoreProject: false);
    }

    public DirectoryInfo GetGlobalConfigDirectory()
    {
        var overridePath = Environment.GetEnvironmentVariable("CODEMERIDIAN_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(overridePath))
            return new DirectoryInfo(overridePath);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
            return new DirectoryInfo(Path.Combine(localAppData, "CodeMeridian"));

        return new DirectoryInfo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codemeridian"));
    }

    public FileInfo GetGlobalConfigFile(DirectoryInfo? globalConfigDirectory = null) =>
        new(Path.Combine((globalConfigDirectory ?? GetGlobalConfigDirectory()).FullName, ConfigFileName));

    public CodeMeridianConfigWriteResult WriteGlobal(string codeMeridianUrl, bool overwrite = false, DirectoryInfo? globalConfigDirectory = null)
    {
        var rootDirectory = globalConfigDirectory ?? GetGlobalConfigDirectory();
        Directory.CreateDirectory(rootDirectory.FullName);

        var filePath = Path.Combine(rootDirectory.FullName, ConfigFileName);
        var defaultRoot = BuildMeridianConfigRoot(project: null, codeMeridianUrl, useGlobalCache: true);
        CodeMeridianConfigWriteResult result;

        if (!File.Exists(filePath))
        {
            WriteJsonFile(filePath, defaultRoot.ToJsonString(WriteOptions));
            result = new CodeMeridianConfigWriteResult(
                Created: true,
                Changed: true,
                BackupPath: null,
                PreviousVersion: 0,
                CurrentVersion: CurrentConfigVersion,
                AddedPaths: ["version"]);
        }
        else if (overwrite)
        {
            result = OverwriteExistingConfig(filePath, defaultRoot);
        }
        else
        {
            var existingRoot = ParseRequiredObject(File.ReadAllText(filePath), filePath);
            var previousVersion = ReadVersion(existingRoot);

            existingRoot["project"] = string.Empty;
            existingRoot["codeMeridianUrl"] = codeMeridianUrl;
            existingRoot["useGlobalCache"] = true;

            var addedPaths = new List<string>();
            MergeMissingNodes(existingRoot, defaultRoot, addedPaths, parentPath: null);
            UpdateVersion(existingRoot, previousVersion, addedPaths);
            var backupPath = addedPaths.Count > 0 || previousVersion != CurrentConfigVersion
                ? WriteJsonFileWithBackup(filePath, existingRoot.ToJsonString(WriteOptions))
                : null;

            result = new CodeMeridianConfigWriteResult(
                Created: false,
                Changed: backupPath is not null,
                BackupPath: backupPath,
                PreviousVersion: previousVersion,
                CurrentVersion: CurrentConfigVersion,
                AddedPaths: addedPaths);
        }

        WriteSchemaFile(rootDirectory, overwrite);
        return result;
    }

    public CodeMeridianConfigWriteResult Write(
        DirectoryInfo rootDirectory,
        string? project,
        string codeMeridianUrl,
        bool useGlobalCache = false,
        bool overwrite = false)
    {
        Directory.CreateDirectory(rootDirectory.FullName);

        var filePath = Path.Combine(rootDirectory.FullName, ConfigFileName);
        var defaultRoot = BuildMeridianConfigRoot(project, codeMeridianUrl, useGlobalCache);

        CodeMeridianConfigWriteResult result;
        if (!File.Exists(filePath))
        {
            WriteJsonFile(filePath, defaultRoot.ToJsonString(WriteOptions));
            result = new CodeMeridianConfigWriteResult(
                Created: true,
                Changed: true,
                BackupPath: null,
                PreviousVersion: 0,
                CurrentVersion: CurrentConfigVersion,
                AddedPaths: ["version"]);
        }
        else if (overwrite)
        {
            result = OverwriteExistingConfig(filePath, defaultRoot);
        }
        else
        {
            var existingRoot = ParseRequiredObject(File.ReadAllText(filePath), filePath);
            var previousVersion = ReadVersion(existingRoot);
            var addedPaths = new List<string>();

            MergeMissingNodes(existingRoot, defaultRoot, addedPaths, parentPath: null);
            UpdateVersion(existingRoot, previousVersion, addedPaths);

            var backupPath = addedPaths.Count > 0 || previousVersion != CurrentConfigVersion
                ? WriteJsonFileWithBackup(filePath, existingRoot.ToJsonString(WriteOptions))
                : null;

            result = new CodeMeridianConfigWriteResult(
                Created: false,
                Changed: backupPath is not null,
                BackupPath: backupPath,
                PreviousVersion: previousVersion,
                CurrentVersion: CurrentConfigVersion,
                AddedPaths: addedPaths);
        }

        WriteSchemaFile(rootDirectory, overwrite);
        return result;
    }

    private CodeMeridianConfigSnapshot? LoadFile(FileInfo configFile, bool ignoreProject)
    {
        try
        {
            var directoryPath = configFile.DirectoryName;
            if (string.IsNullOrWhiteSpace(directoryPath))
                return null;

            var root = ParseRequiredObject(File.ReadAllText(configFile.FullName), configFile.FullName);

            var configuration = new ConfigurationBuilder()
                .SetBasePath(directoryPath)
                .AddJsonFile(configFile.Name, optional: false, reloadOnChange: false)
                .Build();

            var options = configuration.Get<CodeMeridianConfigFileOptions>();
            if (options is null)
                return null;

            return new CodeMeridianConfigSnapshot(
                ignoreProject ? null : NormalizeOptionalString(options.Project),
                NormalizeOptionalString(options.CodeMeridianUrl) ?? NormalizeOptionalString(options.Url),
                options.AllowRepoScripts,
                options.UseGlobalCache,
                NormalizePatterns(options.ConfigurationFiles),
                ReadVersion(root));
        }
        catch
        {
            return null;
        }
    }

    private static JsonObject BuildMeridianConfigRoot(string? project, string codeMeridianUrl, bool useGlobalCache)
    {
        var template = ReadRequiredTemplate(MeridianSampleFileName)
            .Replace("{{project}}", JsonEncodedText.Encode(project ?? string.Empty).ToString(), StringComparison.Ordinal)
            .Replace("{{codeMeridianUrl}}", JsonEncodedText.Encode(codeMeridianUrl).ToString(), StringComparison.Ordinal)
            .Replace("{{useGlobalCache}}", useGlobalCache ? "true" : "false", StringComparison.Ordinal)
            .TrimEnd();

        return ParseRequiredObject(template, MeridianSampleFileName);
    }

    private static void WriteSchemaFile(DirectoryInfo rootDirectory, bool overwrite)
    {
        var targetPath = Path.Combine(rootDirectory.FullName, "meridian.schema.json");
        if (File.Exists(targetPath) && !overwrite)
            return;

        var sourcePath = Path.Combine(AppContext.BaseDirectory, "meridian.schema.json");
        if (File.Exists(sourcePath))
            File.Copy(sourcePath, targetPath, overwrite: true);
    }

    private static string ReadRequiredTemplate(string fileName)
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(sourcePath))
            return File.ReadAllText(sourcePath);

        throw new InvalidOperationException($"Required template file is missing: {sourcePath}");
    }

    private static FileInfo? FindLocalConfig(DirectoryInfo directory)
    {
        for (var current = directory; current is not null; current = current.Parent)
        {
            var configFile = new FileInfo(Path.Combine(current.FullName, ConfigFileName));
            if (configFile.Exists)
                return configFile;
        }

        return null;
    }

    private FileInfo? FindGlobalConfig(DirectoryInfo? globalConfigDirectory)
    {
        var configFile = GetGlobalConfigFile(globalConfigDirectory);
        return configFile.Exists ? configFile : null;
    }

    private static string? NormalizeOptionalString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string>? NormalizePatterns(IEnumerable<string>? values)
    {
        if (values is null)
            return null;

        var normalized = values
            .Select(NormalizeOptionalString)
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0 ? null : normalized;
    }

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private static JsonObject ParseRequiredObject(string json, string sourceName)
    {
        try
        {
            return JsonNode.Parse(
                json,
                new JsonNodeOptions { PropertyNameCaseInsensitive = false },
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                })?.AsObject()
                   ?? throw new InvalidOperationException($"Config file must contain a JSON object: {sourceName}");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException($"Config file is not valid JSON: {sourceName}", ex);
        }
    }

    private static int ReadVersion(JsonObject root)
    {
        if (root["version"] is JsonValue value && value.TryGetValue<int>(out var version))
            return version;

        return 0;
    }

    private static void UpdateVersion(JsonObject root, int previousVersion, List<string> addedPaths)
    {
        if (previousVersion == CurrentConfigVersion)
            return;

        root["version"] = CurrentConfigVersion;
        if (!addedPaths.Contains("version", StringComparer.Ordinal))
            addedPaths.Add("version");
    }

    private static void MergeMissingNodes(JsonObject target, JsonObject defaults, List<string> addedPaths, string? parentPath)
    {
        foreach (var property in defaults)
        {
            var propertyPath = string.IsNullOrEmpty(parentPath) ? property.Key : $"{parentPath}.{property.Key}";
            var defaultNode = property.Value;

            if (!target.TryGetPropertyValue(property.Key, out var existingNode) || existingNode is null)
            {
                target[property.Key] = defaultNode?.DeepClone();
                addedPaths.Add(propertyPath);
                continue;
            }

            if (existingNode is JsonObject existingObject && defaultNode is JsonObject defaultObject)
            {
                MergeMissingNodes(existingObject, defaultObject, addedPaths, propertyPath);
                continue;
            }

            if (existingNode is JsonArray existingArray && defaultNode is JsonArray defaultArray)
            {
                MergeMissingArrayEntries(existingArray, defaultArray, addedPaths, propertyPath);
            }
        }
    }

    private static void MergeMissingArrayEntries(JsonArray target, JsonArray defaults, List<string> addedPaths, string propertyPath)
    {
        var seen = new HashSet<string>(target.Select(ToComparableJson), StringComparer.Ordinal);
        var changed = false;

        foreach (var item in defaults)
        {
            var comparable = ToComparableJson(item);
            if (!seen.Add(comparable))
                continue;

            target.Add(item?.DeepClone());
            changed = true;
        }

        if (changed)
            addedPaths.Add(propertyPath);
    }

    private static string ToComparableJson(JsonNode? node) =>
        node?.ToJsonString() ?? "null";

    private static CodeMeridianConfigWriteResult OverwriteExistingConfig(string filePath, JsonObject defaultRoot)
    {
        var previousVersion = 0;
        var existingJson = File.ReadAllText(filePath);

        try
        {
            previousVersion = ReadVersion(ParseRequiredObject(existingJson, filePath));
        }
        catch
        {
            // Force overwrite should still replace an invalid existing file.
        }

        var backupPath = WriteJsonFileWithBackup(filePath, defaultRoot.ToJsonString(WriteOptions));
        return new CodeMeridianConfigWriteResult(
            Created: false,
            Changed: true,
            BackupPath: backupPath,
            PreviousVersion: previousVersion,
            CurrentVersion: CurrentConfigVersion,
            AddedPaths: ["version"]);
    }

    private static void WriteJsonFile(string filePath, string json)
    {
        File.WriteAllText(filePath, json + Environment.NewLine);
    }

    private static string WriteJsonFileWithBackup(string filePath, string json)
    {
        var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        var backupPath = $"{filePath}.bak";
        File.WriteAllText(tempPath, json + Environment.NewLine);
        File.Replace(tempPath, filePath, backupPath, ignoreMetadataErrors: true);
        return backupPath;
    }
}
