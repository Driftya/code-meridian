using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace CodeMeridian.Tooling.Configuration;

public sealed class CodeMeridianConfigFileStore
{
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

    public void WriteGlobal(string codeMeridianUrl, bool overwrite = false, DirectoryInfo? globalConfigDirectory = null)
    {
        var rootDirectory = globalConfigDirectory ?? GetGlobalConfigDirectory();
        Directory.CreateDirectory(rootDirectory.FullName);

        var filePath = Path.Combine(rootDirectory.FullName, ConfigFileName);
        var json = File.Exists(filePath) && !overwrite
            ? UpdateGlobalMeridianJson(File.ReadAllText(filePath), codeMeridianUrl)
            : BuildMeridianJson(project: null, codeMeridianUrl, useGlobalCache: true);

        File.WriteAllText(filePath, json + Environment.NewLine);
        WriteSchemaFile(rootDirectory, overwrite);
    }

    public void Write(
        DirectoryInfo rootDirectory,
        string? project,
        string codeMeridianUrl,
        bool useGlobalCache = false,
        bool overwrite = false)
    {
        Directory.CreateDirectory(rootDirectory.FullName);

        var filePath = Path.Combine(rootDirectory.FullName, ConfigFileName);
        if (File.Exists(filePath) && !overwrite)
            throw new InvalidOperationException($"Config file already exists: {filePath}. Use --force to overwrite it.");

        var json = BuildMeridianJson(project, codeMeridianUrl, useGlobalCache);
        File.WriteAllText(filePath, json + Environment.NewLine);
        WriteSchemaFile(rootDirectory, overwrite);
    }

    private CodeMeridianConfigSnapshot? LoadFile(FileInfo configFile, bool ignoreProject)
    {
        try
        {
            var directoryPath = configFile.DirectoryName;
            if (string.IsNullOrWhiteSpace(directoryPath))
                return null;

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
                options.UseGlobalCache);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildMeridianJson(string? project, string codeMeridianUrl, bool useGlobalCache)
    {
        var template = ReadRequiredTemplate(MeridianSampleFileName);
        return template
            .Replace("{{project}}", JsonEncodedText.Encode(project ?? string.Empty).ToString(), StringComparison.Ordinal)
            .Replace("{{codeMeridianUrl}}", JsonEncodedText.Encode(codeMeridianUrl).ToString(), StringComparison.Ordinal)
            .Replace("{{useGlobalCache}}", useGlobalCache ? "true" : "false", StringComparison.Ordinal)
            .TrimEnd();
    }

    private static string UpdateGlobalMeridianJson(string existingJson, string codeMeridianUrl)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(
                existingJson,
                new JsonNodeOptions { PropertyNameCaseInsensitive = false },
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                })?.AsObject();
        }
        catch
        {
            root = null;
        }

        root ??= [];
        root["project"] = string.Empty;
        root["codeMeridianUrl"] = codeMeridianUrl;
        root["useGlobalCache"] = true;

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
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
}
