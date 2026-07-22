using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Neo4jCodeGraphRepositoryCollection
{
    public const string Name = "Neo4j code graph repository integration";
}

public abstract class Neo4jCodeGraphRepositoryIntegrationTestBase : IAsyncLifetime
{
    protected readonly Neo4jOptions _options;
    protected Neo4jCodeGraphRepository? _repository;

    public Neo4jCodeGraphRepositoryIntegrationTestBase()
    {
        _options = TestEnvironment.TryGetNeo4jOptions()
            ?? throw new InvalidOperationException("Neo4j connection details were not found in environment or repo .env.");
    }

    public async Task InitializeAsync()
    {
        _repository = new Neo4jCodeGraphRepository(Options.Create(_options), NullLogger<Neo4jCodeGraphRepository>.Instance);
        await _repository.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_repository is not null)
            await _repository.DisposeAsync();
    }

    protected async Task<CodeNode?> FindAnyTargetAsync()
    {
        var matches = await _repository!.QueryNodesAsync(
            new CodeGraphQuery
            {
                ProjectContext = null,
                Limit = 50
            });

        return matches.FirstOrDefault(node =>
            node.Type is CodeNodeType.Class or CodeNodeType.Method or CodeNodeType.File
            && !string.IsNullOrWhiteSpace(node.FilePath));
    }

    protected async Task<CodeNode?> FindNodeWithRelationshipsAsync()
    {
        var matches = await _repository!.QueryNodesAsync(
            new CodeGraphQuery
            {
                ProjectContext = null,
                Limit = 100
            });

        foreach (var node in matches.Where(node => !string.IsNullOrWhiteSpace(node.FilePath)))
        {
            var edges = await _repository.QueryEdgesAsync(node.Id, depth: 1);
            if (edges.Count > 0)
                return node;
        }

        return null;
    }

    protected static CodeNode CreateNode(
        string id,
        string name,
        CodeNodeType type,
        string projectContext,
        string filePath,
        string? namespaceName = null,
        int? lineNumber = null,
        string? summary = null,
        string? sourceHash = null,
        float[]? embedding = null,
        Dictionary<string, string>? properties = null,
        IndexedFileRole fileRole = IndexedFileRole.Unknown)
    {
        return new CodeNode
        {
            Id = id,
            Name = name,
            Type = type,
            ProjectContext = projectContext,
            FilePath = filePath,
            Namespace = namespaceName,
            LineNumber = lineNumber,
            Summary = summary,
            SourceHash = sourceHash,
            Embedding = embedding,
            FileRole = fileRole,
            Properties = properties ?? []
        };
    }

    protected async Task IngestArchitectureConfigAsync(
        string projectContext,
        string architecturePath,
        IReadOnlyDictionary<string, string> entries)
    {
        var meridianFile = CreateNode(
            id: $"{projectContext}::ConfigurationFile::meridian.json",
            name: "meridian.json",
            type: CodeNodeType.ConfigurationFile,
            projectContext: projectContext,
            filePath: "meridian.json");
        var architecturePathKey = CreateNode(
            id: $"{projectContext}::ConfigurationKey::architecture:path",
            name: "architecture:path",
            type: CodeNodeType.ConfigurationKey,
            projectContext: projectContext,
            filePath: "meridian.json");
        var architecturePathEntry = CreateNode(
            id: $"{projectContext}::ConfigurationEntry::meridian.json::architecture-path",
            name: "architecture:path",
            type: CodeNodeType.ConfigurationEntry,
            projectContext: projectContext,
            filePath: "meridian.json",
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["canonicalKey"] = "architecture:path",
                ["rawValuePreview"] = architecturePath
            });
        var architectureFile = CreateNode(
            id: $"{projectContext}::ConfigurationFile::{architecturePath}",
            name: Path.GetFileName(architecturePath),
            type: CodeNodeType.ConfigurationFile,
            projectContext: projectContext,
            filePath: architecturePath);

        await _repository!.UpsertNodeAsync(meridianFile);
        await _repository.UpsertNodeAsync(architecturePathKey);
        await _repository.UpsertNodeAsync(architecturePathEntry);
        await _repository.UpsertNodeAsync(architectureFile);

        await _repository.UpsertEdgeAsync(new CodeEdge
        {
            SourceId = meridianFile.Id,
            TargetId = architecturePathEntry.Id,
            Type = CodeEdgeType.DefinesConfig
        });
        await _repository.UpsertEdgeAsync(new CodeEdge
        {
            SourceId = architecturePathEntry.Id,
            TargetId = architecturePathKey.Id,
            Type = CodeEdgeType.DefinesConfig,
            Properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["valuePreview"] = architecturePath
            }
        });

        var index = 0;
        foreach (var entry in entries)
        {
            var keyId = $"{projectContext}::ConfigurationKey::{entry.Key}";
            var entryId = $"{projectContext}::ConfigurationEntry::{architecturePath}::{index++}";
            await _repository.UpsertNodeAsync(CreateNode(
                keyId,
                entry.Key,
                CodeNodeType.ConfigurationKey,
                projectContext,
                architecturePath));
            await _repository.UpsertNodeAsync(CreateNode(
                entryId,
                entry.Key,
                CodeNodeType.ConfigurationEntry,
                projectContext,
                architecturePath,
                properties: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["canonicalKey"] = entry.Key,
                    ["rawValuePreview"] = entry.Value
                }));
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = architectureFile.Id,
                TargetId = entryId,
                Type = CodeEdgeType.DefinesConfig
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = entryId,
                TargetId = keyId,
                Type = CodeEdgeType.DefinesConfig,
                Properties = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["valuePreview"] = entry.Value
                }
            });
        }
    }
}
