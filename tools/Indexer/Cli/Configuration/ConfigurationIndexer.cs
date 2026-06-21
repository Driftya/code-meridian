using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using CodeMeridian.Application.Services;
using CodeMeridian.Sdk;
using CodeMeridian.Tooling.Configuration;

namespace CodeMeridian.Indexer.Cli.Configuration;

internal sealed class ConfigurationIndexer
{
    private const int FileProgressInterval = 25;
    private const int NodeBatchSize = 100;
    private const int EdgeBatchSize = 200;

    public async Task<int> RunAsync(
        DirectoryInfo rootPath,
        string project,
        string codeMeridianUrl,
        string? apiKey,
        IIndexedFileRoleClassifier fileRoleClassifier,
        IReadOnlyList<string>? configurationFilePatterns,
        string? architecturePath,
        bool clearExistingConfiguration,
        IReadOnlyCollection<string>? changedFiles = null,
        IReadOnlyCollection<string>? deletedFiles = null,
        CancellationToken cancellationToken = default,
        HttpMessageHandler? messageHandler = null)
    {
        using var httpClient = messageHandler is null
            ? new HttpClient()
            : new HttpClient(messageHandler, disposeHandler: false);
        httpClient.BaseAddress = new Uri(codeMeridianUrl);
        httpClient.Timeout = TimeSpan.FromMinutes(10);
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
            .Where(file => ShouldIndexConfigurationFile(rootPath, file, architecturePath))
            .Where(file => changedFiles is null || changedFiles.Contains(Path.GetRelativePath(rootPath.FullName, file.FullName).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase))
            .OrderBy(ConfigurationFilePatternMatcher.GetOrder)
            .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Console.WriteLine($"Indexing configuration batch in {rootPath.FullName}...");
        Console.WriteLine($"  Batch size: {files.Length} file(s)");

        var seenCanonicalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nodeRequests = new List<CodeNodeIngestRequest>();
        var edgeRequests = new List<CodeEdgeIngestRequest>();
        var processedFiles = 0;
        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(rootPath.FullName, file.FullName).Replace('\\', '/');
            if (!ConfigurationFileParser.TryParse(file, rootPath.FullName, out var entries, out var error))
            {
                Console.WriteLine($"  warn: skipped config file '{relativePath}' because it is not valid {file.Extension.TrimStart('.')} content: {error}");
                continue;
            }

            BuildFileRequests(nodeRequests, edgeRequests, project, file, rootPath, entries, seenCanonicalKeys, fileRoleClassifier);
            processedFiles++;
            LogFileProgress(processedFiles, files.Length, relativePath);
        }

        Console.WriteLine($"  Found {nodeRequests.Count} nodes, {edgeRequests.Count} edges");
        await BatchIngestNodesAsync(client, nodeRequests, cancellationToken);
        await BatchIngestEdgesAsync(client, edgeRequests, cancellationToken);

        return 0;
    }

    private static void BuildFileRequests(
        List<CodeNodeIngestRequest> nodeRequests,
        List<CodeEdgeIngestRequest> edgeRequests,
        string project,
        FileInfo file,
        DirectoryInfo rootPath,
        IReadOnlyList<ConfigurationEntryRecord> entries,
        HashSet<string> seenCanonicalKeys,
        IIndexedFileRoleClassifier fileRoleClassifier)
    {
        var relativePath = Path.GetRelativePath(rootPath.FullName, file.FullName).Replace('\\', '/');
        var fileId = $"{project}::ConfigurationFile::{relativePath}";
        var fileRole = fileRoleClassifier.Classify(relativePath).ToString();

        nodeRequests.Add(new CodeNodeIngestRequest(
            fileId,
            file.Name,
            "ConfigurationFile",
            FilePath: relativePath,
            SourceHash: Hash(File.ReadAllText(file.FullName)),
            FileRole: fileRole,
            ProjectContext: project,
            Properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["fileRole"] = fileRole,
                ["format"] = file.Extension.TrimStart('.').ToLowerInvariant(),
                ["sourceKind"] = "configuration-file"
            }));

        foreach (var entry in entries)
        {
            var canonicalKeyId = $"{project}::ConfigurationKey::{entry.CanonicalKey}";
            var entryId = $"{project}::ConfigurationEntry::{relativePath}::{Hash($"{entry.RawKey}|{entry.SourceKind}|{entry.CanonicalKey}")}";

            nodeRequests.Add(new CodeNodeIngestRequest(
                canonicalKeyId,
                entry.CanonicalKey,
                "ConfigurationKey",
                ProjectContext: project,
                Properties: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["canonicalKey"] = entry.CanonicalKey,
                    ["normalizedKey"] = entry.CanonicalKey.ToLowerInvariant(),
                    ["isSecretLike"] = entry.IsSecretLike ? "true" : "false"
                }));

            nodeRequests.Add(new CodeNodeIngestRequest(
                entryId,
                entry.RawKey,
                "ConfigurationEntry",
                FilePath: relativePath,
                FileRole: fileRole,
                ProjectContext: project,
                Properties: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["fileRole"] = fileRole,
                    ["canonicalKey"] = entry.CanonicalKey,
                    ["rawKey"] = entry.RawKey,
                    ["rawValuePreview"] = entry.ValuePreview,
                    ["format"] = entry.Format,
                    ["sourceKind"] = entry.SourceKind,
                    ["valueType"] = entry.ValueType,
                    ["isSecretLike"] = entry.IsSecretLike ? "true" : "false"
                }));

            edgeRequests.Add(new CodeEdgeIngestRequest(
                fileId,
                entryId,
                "DefinesConfig",
                Properties: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["rawKey"] = entry.RawKey,
                    ["sourceKind"] = entry.SourceKind
                }));

            var relationshipType = seenCanonicalKeys.Add(entry.CanonicalKey)
                ? "DefinesConfig"
                : "OverridesConfig";

            edgeRequests.Add(new CodeEdgeIngestRequest(
                entryId,
                canonicalKeyId,
                relationshipType,
                Properties: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["rawKey"] = entry.RawKey,
                    ["sourceKind"] = entry.SourceKind,
                    ["valuePreview"] = entry.ValuePreview
                }));
        }
    }

    private static void LogFileProgress(int processedFiles, int totalFiles, string relativePath)
    {
        if (processedFiles == totalFiles || processedFiles % FileProgressInterval == 0)
            Console.WriteLine($"  Processed {processedFiles}/{totalFiles} configuration files ({relativePath})");
    }

    private static async Task BatchIngestNodesAsync(
        CodeMeridianClient client,
        IReadOnlyList<CodeNodeIngestRequest> nodeRequests,
        CancellationToken cancellationToken)
    {
        var batches = nodeRequests.Chunk(NodeBatchSize).ToArray();
        for (var i = 0; i < batches.Length; i++)
        {
            Console.WriteLine($"  Ingesting configuration nodes batch {i + 1}/{batches.Length}...");
            await client.IngestCodeNodesAsync(batches[i], cancellationToken);
            Console.WriteLine($"  Uploaded {Math.Min((i + 1) * NodeBatchSize, nodeRequests.Count)}/{nodeRequests.Count} configuration nodes");
        }
    }

    private static async Task BatchIngestEdgesAsync(
        CodeMeridianClient client,
        IReadOnlyList<CodeEdgeIngestRequest> edgeRequests,
        CancellationToken cancellationToken)
    {
        var batches = edgeRequests.Chunk(EdgeBatchSize).ToArray();
        for (var i = 0; i < batches.Length; i++)
        {
            Console.WriteLine($"  Ingesting configuration edges batch {i + 1}/{batches.Length}...");
            await client.IngestRelationshipsAsync(batches[i], cancellationToken);
            Console.WriteLine($"  Uploaded {Math.Min((i + 1) * EdgeBatchSize, edgeRequests.Count)}/{edgeRequests.Count} configuration edges");
        }
    }

    private static IReadOnlyCollection<string> FilterConfigurationPaths(IEnumerable<string> paths, IReadOnlyList<string>? patterns) =>
        paths
            .Where(path => ConfigurationFilePatternMatcher.IsConfigurationFile(new FileInfo(path), patterns))
            .ToArray();

    private static bool ShouldIndexConfigurationFile(DirectoryInfo rootPath, FileInfo file, string? architecturePath)
    {
        var relativePath = Path.GetRelativePath(rootPath.FullName, file.FullName).Replace('\\', '/');
        if (!relativePath.StartsWith(".meridian/architectures/", StringComparison.OrdinalIgnoreCase))
            return true;

        var activeArchitecturePath = string.IsNullOrWhiteSpace(architecturePath)
            ? CodeMeridianConfigFileStore.DefaultArchitecturePath
            : architecturePath.Replace('\\', '/');

        return string.Equals(relativePath, activeArchitecturePath, StringComparison.OrdinalIgnoreCase);
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
