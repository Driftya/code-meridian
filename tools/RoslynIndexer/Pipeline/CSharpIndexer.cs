using CodeMeridian.Sdk;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodeMeridian.RoslynIndexer.Pipeline;

/// <summary>
/// Orchestrates Roslyn indexing for .cs files.
/// </summary>
public sealed class CSharpIndexer(
    CodeMeridianClient client,
    IIndexedFileRoleClassifier fileRoleClassifier,
    IOptions<DatabaseTracingOptions> databaseTracingOptions,
    ILogger<CSharpIndexer> logger)
{
    public CSharpIndexer(
        CodeMeridianClient client,
        ILogger<CSharpIndexer> logger)
        : this(
            client,
            new ConfiguredIndexedFileRoleClassifier(Microsoft.Extensions.Options.Options.Create(new CodebaseIndexingOptions())),
            Options.Create(new DatabaseTracingOptions()),
            logger)
    {
    }

    public async Task<IndexStats> IndexAsync(
        FileInfo[] files,
        string projectContext,
        string rootPath,
        CancellationToken cancellationToken = default,
        FileInfo[]? resolutionFiles = null,
        bool isIncremental = false,
        bool refreshCanonicalTypes = false)
    {
        var usedFullResolutionCatalog = !isIncremental || resolutionFiles is not null;
        resolutionFiles ??= files;
        var nodes = new List<IngestNodeRequest>();
        var edges = new List<IngestEdgeRequest>();
        var configurationConstants = CSharpConfigurationConstantRegistry.Build(resolutionFiles);
        LogClassificationSummary(files, rootPath, projectContext, fileRoleClassifier, logger);

        foreach (var file in resolutionFiles)
        {
            try
            {
                ExtractFromFile(file, rootPath, projectContext, nodes, edges, configurationConstants, databaseTracingOptions.Value);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Skipped {File}: {Error}", file.Name, ex.Message);
            }
        }

        nodes = AggregateCanonicalTypes(nodes);
        ApplyFileRoles(nodes, fileRoleClassifier);

        var callResolution = CSharpCallEdgeResolver.ResolveWithDiagnostics(nodes, edges);
        var referenceResolution = CSharpReferenceEdgeResolver.ResolveWithDiagnostics(nodes, callResolution.Edges);
        edges = referenceResolution.Edges;
        LogResolutionSummary("call", callResolution, logger);
        LogResolutionSummary("type reference", referenceResolution, logger);

        var ingestPaths = files
            .Select(file => Path.GetRelativePath(rootPath, file.FullName).Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nodesToIngest = SelectNodesToIngest(nodes, edges, ingestPaths, refreshCanonicalTypes);

        await CSharpBatchIngestionWriter.BatchIngestNodesAsync(client, logger, nodesToIngest, projectContext, cancellationToken);
        await CSharpBatchIngestionWriter.BatchIngestEdgesAsync(client, logger, edges, cancellationToken);

        var unresolved = callResolution.UnresolvedByReason
            .Concat(referenceResolution.UnresolvedByReason)
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Value), StringComparer.Ordinal);
        var mode = isIncremental ? "incremental" : "full";
        var stats = new IndexStats(
            nodesToIngest.Count,
            edges.Count,
            resolutionFiles.Length,
            files.Length,
            callResolution.Attempted,
            callResolution.Resolved,
            referenceResolution.Attempted,
            referenceResolution.Resolved,
            unresolved,
            mode,
            usedFullResolutionCatalog);
        await PersistIndexRunAsync(client, logger, projectContext, stats, cancellationToken);
        return stats;
    }

    private static Task PersistIndexRunAsync(
        CodeMeridianClient client,
        ILogger logger,
        string projectContext,
        IndexStats stats,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["externalKind"] = "IndexRun",
            ["mode"] = stats.Mode,
            ["completedAt"] = now.ToString("O", CultureInfo.InvariantCulture),
            ["scannedFileCount"] = stats.ScannedFiles.ToString(CultureInfo.InvariantCulture),
            ["ingestedFileCount"] = stats.IngestedFiles.ToString(CultureInfo.InvariantCulture),
            ["attemptedCallEdges"] = stats.AttemptedCallEdges.ToString(CultureInfo.InvariantCulture),
            ["resolvedCallEdges"] = stats.ResolvedCallEdges.ToString(CultureInfo.InvariantCulture),
            ["attemptedReferenceEdges"] = stats.AttemptedReferenceEdges.ToString(CultureInfo.InvariantCulture),
            ["resolvedReferenceEdges"] = stats.ResolvedReferenceEdges.ToString(CultureInfo.InvariantCulture),
            ["unresolvedEdgesByReason"] = JsonSerializer.Serialize(stats.UnresolvedEdgesByReason),
            ["usedFullResolutionCatalog"] = stats.UsedFullResolutionCatalog.ToString(CultureInfo.InvariantCulture)
        };
        var run = new IngestNodeRequest(
            $"{projectContext}::IndexRun::{stats.Mode}",
            $"{stats.Mode} C# index run",
            "Diagnostic",
            null,
            null,
            null,
            $"Scanned {stats.ScannedFiles} file(s), ingested {stats.IngestedFiles}, and resolved {stats.ResolvedCallEdges + stats.ResolvedReferenceEdges} relationship(s).",
            Properties: properties);

        logger.LogInformation(
            "Recording {Mode} index-run metadata with {ScannedFiles} scanned and {IngestedFiles} ingested file(s).",
            stats.Mode,
            stats.ScannedFiles,
            stats.IngestedFiles);
        return CSharpBatchIngestionWriter.BatchIngestNodesAsync(client, logger, [run], projectContext, cancellationToken);
    }

    private static List<IngestNodeRequest> SelectNodesToIngest(
        IReadOnlyCollection<IngestNodeRequest> nodes,
        IReadOnlyCollection<IngestEdgeRequest> edges,
        IReadOnlySet<string> ingestPaths,
        bool refreshCanonicalTypes)
    {
        var selectedIds = nodes
            .Where(node => IsDeclaredInIngestPaths(node, ingestPaths)
                || refreshCanonicalTypes && IsCanonicalType(node.Type))
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var edge in edges.Where(edge => selectedIds.Contains(edge.SourceId)))
            {
                var target = nodes.FirstOrDefault(node => node.Id == edge.TargetId);
                if (target is not null && target.FilePath is null && selectedIds.Add(target.Id))
                    changed = true;
            }
        }

        return nodes.Where(node => selectedIds.Contains(node.Id)).ToList();
    }

    private static bool IsCanonicalType(string nodeType) =>
        nodeType is "Class" or "Interface" or "Struct" or "Enum" or "Delegate";

    private static bool IsDeclaredInIngestPaths(IngestNodeRequest node, IReadOnlySet<string> ingestPaths)
    {
        if (node.FilePath is not null && ingestPaths.Contains(node.FilePath))
            return true;

        if (node.Properties is null
            || !node.Properties.TryGetValue("declarationPaths", out var serializedPaths))
        {
            return false;
        }

        return JsonSerializer.Deserialize<string[]>(serializedPaths)?.Any(ingestPaths.Contains) == true;
    }

    private static List<IngestNodeRequest> AggregateCanonicalTypes(IReadOnlyList<IngestNodeRequest> nodes)
    {
        var aggregateTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Class", "Interface", "Struct", "Enum", "Delegate"
        };
        var aggregateById = nodes
            .Where(node => aggregateTypes.Contains(node.Type))
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, AggregateCanonicalType, StringComparer.Ordinal);

        return nodes
            .Where(node => !aggregateTypes.Contains(node.Type))
            .Concat(aggregateById.Values)
            .ToList();
    }

    private static IngestNodeRequest AggregateCanonicalType(IGrouping<string, IngestNodeRequest> declarations)
    {
        var ordered = declarations
            .OrderBy(node => node.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.LineNumber)
            .ToArray();
        var primary = ordered[0];
        var declarationPaths = ordered
            .Select(node => node.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var properties = primary.Properties is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(primary.Properties, StringComparer.Ordinal);
        properties["declarationCount"] = ordered.Length.ToString(CultureInfo.InvariantCulture);
        properties["declarationPaths"] = JsonSerializer.Serialize(declarationPaths);
        properties["primaryLocationKind"] = "deterministic_lexical_path";

        var hashPayload = string.Join(
            "\n",
            ordered.Select(node => $"{node.FilePath}\0{node.SourceHash}"));
        var aggregateHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashPayload)))
            .ToLowerInvariant();

        return primary with
        {
            LineCount = ordered.Sum(node => node.LineCount ?? 0),
            SourceHash = aggregateHash,
            Summary = ordered.Select(node => node.Summary).FirstOrDefault(summary => !string.IsNullOrWhiteSpace(summary)),
            Properties = properties
        };
    }

    private static void LogResolutionSummary(string edgeKind, EdgeResolutionResult result, ILogger logger)
    {
        if (result.Attempted == 0)
            return;

        logger.LogInformation(
            "Resolved {Resolved}/{Attempted} local {EdgeKind} edge(s). Unresolved: {UnresolvedByReason}",
            result.Resolved,
            result.Attempted,
            edgeKind,
            result.UnresolvedByReason.Count == 0
                ? "none"
                : string.Join(", ", result.UnresolvedByReason.OrderBy(item => item.Key).Select(item => $"{item.Key}={item.Value}")));
    }

    private static void ApplyFileRoles(List<IngestNodeRequest> nodes, IIndexedFileRoleClassifier fileRoleClassifier)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (string.IsNullOrWhiteSpace(node.FilePath))
                continue;

            var properties = node.Properties is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(node.Properties, StringComparer.Ordinal);
            properties["fileRole"] = fileRoleClassifier.Classify(node.FilePath).ToString();
            nodes[i] = node with { Properties = properties };
        }
    }

    private static void LogClassificationSummary(
        IEnumerable<FileInfo> files,
        string rootPath,
        string projectContext,
        IIndexedFileRoleClassifier fileRoleClassifier,
        ILogger logger)
    {
        var counts = Enum.GetValues<IndexedFileRole>().ToDictionary(role => role, _ => 0);
        var fileCount = 0;

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(rootPath, file.FullName).Replace('\\', '/');
            counts[fileRoleClassifier.Classify(relativePath)]++;
            fileCount++;
        }

        logger.LogInformation(
            "Classified {FileCount} indexed files for project {ProjectName}: Source={SourceCount}, Test={TestCount}, Migration={MigrationCount}, Snapshot={SnapshotCount}, Generated={GeneratedCount}, BuildArtifact={BuildArtifactCount}, Configuration={ConfigurationCount}, Unknown={UnknownCount}",
            fileCount,
            projectContext,
            counts[IndexedFileRole.Source],
            counts[IndexedFileRole.Test],
            counts[IndexedFileRole.Migration],
            counts[IndexedFileRole.Snapshot],
            counts[IndexedFileRole.Generated],
            counts[IndexedFileRole.BuildArtifact],
            counts[IndexedFileRole.Configuration],
            counts[IndexedFileRole.Unknown]);
    }

    private static void ExtractFromFile(
        FileInfo file,
        string rootPath,
        string projectContext,
        List<IngestNodeRequest> nodes,
        List<IngestEdgeRequest> edges,
        CSharpConfigurationConstantRegistry configurationConstants,
        DatabaseTracingOptions databaseTracingOptions)
    {
        var source = File.ReadAllText(file.FullName);
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source, path: file.FullName);
        var root = tree.GetCompilationUnitRoot();

        var relPath = Path.GetRelativePath(rootPath, file.FullName).Replace('\\', '/');
        var walker = new CSharpAstWalker(relPath, projectContext, nodes, edges);
        walker.Visit(root);
        CSharpRouteExtractor.Extract(root, relPath, projectContext, nodes, edges);
        CSharpConfigurationUsageExtractor.Extract(root, relPath, projectContext, nodes, edges, configurationConstants);
        CSharpDatabaseTracingExtractor.Extract(root, relPath, projectContext, nodes, edges, configurationConstants, databaseTracingOptions);
    }
}
