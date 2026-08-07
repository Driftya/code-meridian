using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Microsoft.Extensions.Configuration;

namespace CodeMeridian.Tooling.Configuration;

public sealed class CodeMeridianConfigFileStore
{
    public const int CurrentConfigVersion = 2;

    public const string DefaultArchitecturePath = ".meridian/architecture.json";
    public const string DefaultArchitectureTemplateFileName = "architecture.clean.template.json";
    public const string DefaultAgentCapabilitiesDirectory = "meridian-agent-capabilities";
    public const string DefaultKeywordClassificationPath = ".meridian/keyword-classification.json";
    public const string DefaultKeywordClassificationSamplePath = "keyword-classification.sample.json";
    public const string DefaultDatabaseTracingPath = ".meridian/database-tracing.json";
    public const string DefaultDatabaseTracingSamplePath = "database-tracing.sample.json";

    private const string MeridianSampleFileName = "meridian.sample.json";
    private const string ConfigFileName = "meridian.json";
    private const string ConfigSchemaResourceName = "CodeMeridian.Tooling.meridian.schema.json";
    private static readonly Lazy<JsonSchema> ConfigSchema = new(LoadConfigSchema);
    private static readonly string[] ArchitectureTemplateFileNames =
    [
        "architecture.clean.template.json",
        "architecture.onion.template.json",
        "architecture.hexagonal.template.json",
        "architecture.layered.template.json",
        "architecture.vertical-slice.template.json"
    ];

    public IReadOnlyList<string> GetArchitectureTemplateFileNames() => ArchitectureTemplateFileNames;

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
        WriteArchitectureTemplates(rootDirectory, overwrite);
        WriteKeywordClassificationFiles(rootDirectory, overwrite);
        WriteDatabaseTracingFiles(rootDirectory, overwrite);
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
        WriteArchitectureTemplates(rootDirectory, overwrite);
        WriteKeywordClassificationFiles(rootDirectory, overwrite);
        WriteDatabaseTracingFiles(rootDirectory, overwrite);
        return result;
    }

    private CodeMeridianConfigSnapshot LoadFile(FileInfo configFile, bool ignoreProject)
    {
        try
        {
            var directoryPath = configFile.DirectoryName;
            if (string.IsNullOrWhiteSpace(directoryPath))
                throw new InvalidOperationException("The configuration file has no parent directory.");

            var json = File.ReadAllText(configFile.FullName);
            using var document = ParseRequiredDocument(json, configFile.FullName);
            var root = ParseRequiredObject(document.RootElement, configFile.FullName);
            ValidateAgainstSchema(document.RootElement);

            var configuration = new ConfigurationBuilder()
                .SetBasePath(directoryPath)
                .AddJsonFile(configFile.Name, optional: false, reloadOnChange: false)
                .Build();

            var options = configuration.Get<CodeMeridianConfigFileOptions>();
            if (options is null)
                throw new InvalidOperationException("The configuration could not be mapped to supported options.");

            return new CodeMeridianConfigSnapshot(
                ignoreProject ? null : NormalizeOptionalString(options.Project),
                NormalizeOptionalString(options.CodeMeridianUrl) ?? NormalizeOptionalString(options.Url),
                options.AllowRepoScripts,
                options.UseGlobalCache,
                NormalizePatterns(options.ConfigurationFiles),
                NormalizeOptionalString(options.Architecture?.Path),
                NormalizeFileRolePatterns(options.Indexing?.FileRoles),
                ReadVersion(root),
                options.Embedding?.Enabled);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Invalid CodeMeridian configuration file '{configFile.FullName}': {ex.Message}",
                ex);
        }
    }

    private static void ValidateAgainstSchema(JsonElement root)
    {
        var result = ConfigSchema.Value.Evaluate(
            root,
            new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (result.IsValid)
            return;

        throw new InvalidOperationException(
            $"The configuration does not match meridian.schema.json. {DescribeSchemaFailure(result)}");
    }

    private static string DescribeSchemaFailure(EvaluationResults result)
    {
        if (result.Errors is { Count: > 0 })
        {
            var location = result.InstanceLocation.ToString();
            var displayLocation = string.IsNullOrEmpty(location) ? "$" : $"${location}";
            return $"{displayLocation}: {string.Join("; ", result.Errors.Values)}";
        }

        if (result.Details is null)
            return "No further validation details were provided.";

        foreach (var detail in result.Details)
        {
            if (!detail.IsValid)
                return DescribeSchemaFailure(detail);
        }

        return "No further validation details were provided.";
    }

    private static JsonSchema LoadConfigSchema()
    {
        using var stream = typeof(CodeMeridianConfigFileStore).Assembly
            .GetManifestResourceStream(ConfigSchemaResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded configuration schema '{ConfigSchemaResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
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

    private static void WriteArchitectureTemplates(DirectoryInfo rootDirectory, bool overwrite)
    {
        var meridianDirectory = Directory.CreateDirectory(Path.Combine(rootDirectory.FullName, ".meridian"));
        var architecturesDirectory = Directory.CreateDirectory(Path.Combine(meridianDirectory.FullName, "architectures"));

        foreach (var templateFileName in ArchitectureTemplateFileNames)
        {
            WriteTemplateFile(
                new FileInfo(Path.Combine(architecturesDirectory.FullName, templateFileName)),
                ReadRequiredTemplate(Path.Combine("architectures", templateFileName)),
                overwrite);
        }

        WriteTemplateFile(
            new FileInfo(Path.Combine(meridianDirectory.FullName, "architecture.json")),
            ReadRequiredTemplate(Path.Combine("architectures", DefaultArchitectureTemplateFileName)),
            overwrite: false);
    }

    private static void WriteKeywordClassificationFiles(DirectoryInfo rootDirectory, bool overwrite)
    {
        var meridianDirectory = Directory.CreateDirectory(Path.Combine(rootDirectory.FullName, ".meridian"));

        WriteTemplateFile(
            new FileInfo(Path.Combine(meridianDirectory.FullName, "keyword-classification.json")),
            ReadRequiredTemplate(DefaultKeywordClassificationSamplePath),
            overwrite);
    }

    private static void WriteDatabaseTracingFiles(DirectoryInfo rootDirectory, bool overwrite)
    {
        var meridianDirectory = Directory.CreateDirectory(Path.Combine(rootDirectory.FullName, ".meridian"));

        WriteTemplateFile(
            new FileInfo(Path.Combine(meridianDirectory.FullName, "database-tracing.json")),
            ReadRequiredTemplate(DefaultDatabaseTracingSamplePath),
            overwrite);
    }

    public void WriteAgentCapabilities(DirectoryInfo rootDirectory, bool overwrite)
    {
        var targetDirectory = new DirectoryInfo(Path.Combine(rootDirectory.FullName, DefaultAgentCapabilitiesDirectory));
        CopyRequiredTemplateDirectory(
            Path.Combine("docs", "agent-capabilities"),
            targetDirectory,
            overwrite);
        CopyRequiredTemplateDirectory(
            Path.Combine("scripts", DefaultAgentCapabilitiesDirectory),
            targetDirectory,
            overwrite);
    }

    private static void CopyRequiredTemplateDirectory(string relativeSourcePath, DirectoryInfo targetDirectory, bool overwrite)
    {
        var sourceDirectory = new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, relativeSourcePath));
        if (!sourceDirectory.Exists)
            throw new InvalidOperationException($"Required template directory is missing: {sourceDirectory.FullName}");

        CopyDirectory(sourceDirectory, targetDirectory, overwrite);
    }

    private static string ReadRequiredTemplate(string fileName)
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(sourcePath))
            return File.ReadAllText(sourcePath);

        throw new InvalidOperationException($"Required template file is missing: {sourcePath}");
    }

    private static void WriteTemplateFile(FileInfo targetFile, string content, bool overwrite)
    {
        if (targetFile.Exists && !overwrite)
            return;

        Directory.CreateDirectory(targetFile.DirectoryName!);
        File.WriteAllText(targetFile.FullName, content.TrimEnd() + Environment.NewLine);
    }

    private static void CopyDirectory(DirectoryInfo sourceDirectory, DirectoryInfo targetDirectory, bool overwrite)
    {
        Directory.CreateDirectory(targetDirectory.FullName);

        foreach (var file in sourceDirectory.GetFiles())
        {
            var targetFile = new FileInfo(Path.Combine(targetDirectory.FullName, file.Name));
            if (targetFile.Exists && !overwrite)
                continue;

            file.CopyTo(targetFile.FullName, overwrite: true);
        }

        foreach (var subdirectory in sourceDirectory.GetDirectories())
        {
            CopyDirectory(
                subdirectory,
                new DirectoryInfo(Path.Combine(targetDirectory.FullName, subdirectory.Name)),
                overwrite);
        }
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

    private static CodeMeridianFileRolePatternSnapshot? NormalizeFileRolePatterns(CodeMeridianFileRoleOptions? options)
    {
        if (options is null)
            return null;

        return new CodeMeridianFileRolePatternSnapshot(
            NormalizePatterns(options.Test),
            NormalizePatterns(options.Migration),
            NormalizePatterns(options.Snapshot),
            NormalizePatterns(options.Generated),
            NormalizePatterns(options.BuildArtifact),
            NormalizePatterns(options.Configuration));
    }

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private static JsonObject ParseRequiredObject(string json, string sourceName)
    {
        using var document = ParseRequiredDocument(json, sourceName);
        return ParseRequiredObject(document.RootElement, sourceName);
    }

    private static JsonDocument ParseRequiredDocument(string json, string sourceName)
    {
        try
        {
            var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
            ValidateNoDuplicateProperties(document.RootElement, "$", sourceName);
            return document;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Config file is not valid JSON: {sourceName}. {ex.Message}",
                ex);
        }
    }

    private static JsonObject ParseRequiredObject(JsonElement root, string sourceName)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Config file must contain a JSON object: {sourceName}");

        return JsonNode.Parse(
            root.GetRawText(),
            new JsonNodeOptions { PropertyNameCaseInsensitive = false },
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            })!.AsObject();
    }

    private static void ValidateNoDuplicateProperties(JsonElement element, string path, string sourceName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!properties.Add(property.Name))
                {
                    throw new InvalidOperationException(
                        $"Config file contains duplicate property '{property.Name}' at {path}: {sourceName}");
                }

                ValidateNoDuplicateProperties(property.Value, $"{path}.{property.Name}", sourceName);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Array)
            return;

        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            ValidateNoDuplicateProperties(item, $"{path}[{index}]", sourceName);
            index++;
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
