using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using CodeMeridian.Sdk;

namespace CodeMeridian.Indexer.Cli.Configuration;

internal sealed class ConfigurationIndexer
{
    public async Task<int> RunAsync(
        DirectoryInfo rootPath,
        string project,
        string codeMeridianUrl,
        string? apiKey,
        IReadOnlyList<string>? configurationFilePatterns,
        bool clearExistingConfiguration,
        IReadOnlyCollection<string>? changedFiles = null,
        IReadOnlyCollection<string>? deletedFiles = null,
        CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(codeMeridianUrl),
            Timeout = TimeSpan.FromMinutes(10)
        };
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(apiKey))
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var client = new CodeMeridianClient(httpClient);

        if (clearExistingConfiguration)
            await client.ClearProjectConfigurationAsync(project, cancellationToken);

        var relevantDeletes = FilterConfigurationPaths(deletedFiles ?? [], configurationFilePatterns);
        if (changedFiles is not null)
        {
            var staleFiles = FilterConfigurationPaths(changedFiles, configurationFilePatterns).Concat(relevantDeletes).ToArray();
            await new Commands.ProjectFileDeletionService(codeMeridianUrl, apiKey, project, rootPath).DeleteAsync(staleFiles);
        }

        var files = rootPath
            .EnumerateFiles("*.*", SearchOption.AllDirectories)
            .Where(file => !Commands.IndexExecutionPlanBuilder.IsIgnoredPath(rootPath, file))
            .Where(file => ConfigurationFilePatternMatcher.IsConfigurationFile(file, configurationFilePatterns))
            .Where(file => changedFiles is null || changedFiles.Contains(Path.GetRelativePath(rootPath.FullName, file.FullName).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase))
            .OrderBy(ConfigurationFilePatternMatcher.GetOrder)
            .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var seenCanonicalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!ConfigurationFileParser.TryParse(file, rootPath.FullName, out var entries, out var error))
            {
                var relativePath = Path.GetRelativePath(rootPath.FullName, file.FullName).Replace('\\', '/');
                Console.WriteLine($"  warn: skipped config file '{relativePath}' because it is not valid {file.Extension.TrimStart('.')} content: {error}");
                continue;
            }

            await IngestFileAsync(client, project, file, rootPath, entries, seenCanonicalKeys, cancellationToken);
        }

        return 0;
    }

    private static async Task IngestFileAsync(
        CodeMeridianClient client,
        string project,
        FileInfo file,
        DirectoryInfo rootPath,
        IReadOnlyList<ConfigurationEntryRecord> entries,
        HashSet<string> seenCanonicalKeys,
        CancellationToken cancellationToken)
    {
        var relativePath = Path.GetRelativePath(rootPath.FullName, file.FullName).Replace('\\', '/');
        var fileId = $"{project}::ConfigurationFile::{relativePath}";

        await client.IngestCodeNodeAsync(
            fileId,
            file.Name,
            "ConfigurationFile",
            filePath: relativePath,
            projectContext: project,
            sourceHash: Hash(File.ReadAllText(file.FullName)),
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["format"] = file.Extension.TrimStart('.').ToLowerInvariant(),
                ["sourceKind"] = "configuration-file"
            },
            cancellationToken: cancellationToken);

        foreach (var entry in entries)
        {
            var canonicalKeyId = $"{project}::ConfigurationKey::{entry.CanonicalKey}";
            var entryId = $"{project}::ConfigurationEntry::{relativePath}::{Hash($"{entry.RawKey}|{entry.SourceKind}|{entry.CanonicalKey}")}";

            await client.IngestCodeNodeAsync(
                canonicalKeyId,
                entry.CanonicalKey,
                "ConfigurationKey",
                projectContext: project,
                properties: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["canonicalKey"] = entry.CanonicalKey,
                    ["normalizedKey"] = entry.CanonicalKey.ToLowerInvariant(),
                    ["isSecretLike"] = entry.IsSecretLike ? "true" : "false"
                },
                cancellationToken: cancellationToken);

            await client.IngestCodeNodeAsync(
                entryId,
                entry.RawKey,
                "ConfigurationEntry",
                filePath: relativePath,
                projectContext: project,
                properties: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["canonicalKey"] = entry.CanonicalKey,
                    ["rawKey"] = entry.RawKey,
                    ["rawValuePreview"] = entry.ValuePreview,
                    ["format"] = entry.Format,
                    ["sourceKind"] = entry.SourceKind,
                    ["valueType"] = entry.ValueType,
                    ["isSecretLike"] = entry.IsSecretLike ? "true" : "false"
                },
                cancellationToken: cancellationToken);

            await client.IngestRelationshipAsync(
                fileId,
                entryId,
                "DefinesConfig",
                properties: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["rawKey"] = entry.RawKey,
                    ["sourceKind"] = entry.SourceKind
                },
                cancellationToken: cancellationToken);

            var relationshipType = seenCanonicalKeys.Add(entry.CanonicalKey)
                ? "DefinesConfig"
                : "OverridesConfig";

            await client.IngestRelationshipAsync(
                entryId,
                canonicalKeyId,
                relationshipType,
                properties: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["rawKey"] = entry.RawKey,
                    ["sourceKind"] = entry.SourceKind,
                    ["valuePreview"] = entry.ValuePreview
                },
                cancellationToken: cancellationToken);
        }
    }

    private static IReadOnlyCollection<string> FilterConfigurationPaths(IEnumerable<string> paths, IReadOnlyList<string>? patterns) =>
        paths
            .Where(path => ConfigurationFilePatternMatcher.IsConfigurationFile(new FileInfo(path), patterns))
            .ToArray();

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
