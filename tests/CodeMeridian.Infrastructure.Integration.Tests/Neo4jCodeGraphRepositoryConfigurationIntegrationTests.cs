using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

public sealed class Neo4jCodeGraphRepositoryConfigurationIntegrationTests : IAsyncLifetime
{
    private readonly Neo4jOptions _options;
    private Neo4jCodeGraphRepository? _repository;

    public Neo4jCodeGraphRepositoryConfigurationIntegrationTests()
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

    [Fact]
    public async Task FindConfigDefinitionsAsync_WithTemporaryFixture_ReturnsDefinitionsAndOverrides()
    {
        var projectContext = $"Integration.ConfigDefinitions.{Guid.NewGuid():N}";
        var keyNode = ConfigNode($"{projectContext}.Key", "Neo4j:Uri", CodeNodeType.ConfigurationKey, projectContext)
            with
            {
                Properties = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["canonicalKey"] = "Neo4j:Uri",
                    ["normalizedKey"] = "neo4j:uri"
                }
            };
        var baseFile = ConfigNode($"{projectContext}.BaseFile", "appsettings.json", CodeNodeType.ConfigurationFile, projectContext, "appsettings.json");
        var overrideFile = ConfigNode($"{projectContext}.OverrideFile", "appsettings.Development.json", CodeNodeType.ConfigurationFile, projectContext, "appsettings.Development.json");
        var baseEntry = ConfigNode($"{projectContext}.BaseEntry", "Neo4j:Uri", CodeNodeType.ConfigurationEntry, projectContext, "appsettings.json")
            with
            {
                Properties = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["rawKey"] = "Neo4j:Uri",
                    ["rawValuePreview"] = "bolt://localhost:7687",
                    ["sourceKind"] = "json-path"
                }
            };
        var overrideEntry = ConfigNode($"{projectContext}.OverrideEntry", "Neo4j__Uri", CodeNodeType.ConfigurationEntry, projectContext, "appsettings.Development.json")
            with
            {
                Properties = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["rawKey"] = "Neo4j__Uri",
                    ["rawValuePreview"] = "bolt://dev:7687",
                    ["sourceKind"] = "json-path"
                }
            };

        try
        {
            await _repository!.UpsertNodeAsync(keyNode);
            await _repository.UpsertNodeAsync(baseFile);
            await _repository.UpsertNodeAsync(overrideFile);
            await _repository.UpsertNodeAsync(baseEntry);
            await _repository.UpsertNodeAsync(overrideEntry);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = baseFile.Id,
                TargetId = baseEntry.Id,
                Type = CodeEdgeType.DefinesConfig,
                Properties = new Dictionary<string, string> { ["rawKey"] = "Neo4j:Uri", ["sourceKind"] = "json-path" }
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = overrideFile.Id,
                TargetId = overrideEntry.Id,
                Type = CodeEdgeType.DefinesConfig,
                Properties = new Dictionary<string, string> { ["rawKey"] = "Neo4j__Uri", ["sourceKind"] = "json-path" }
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = baseEntry.Id,
                TargetId = keyNode.Id,
                Type = CodeEdgeType.DefinesConfig,
                Properties = new Dictionary<string, string> { ["rawKey"] = "Neo4j:Uri", ["sourceKind"] = "json-path", ["valuePreview"] = "bolt://localhost:7687" }
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = overrideEntry.Id,
                TargetId = keyNode.Id,
                Type = CodeEdgeType.OverridesConfig,
                Properties = new Dictionary<string, string> { ["rawKey"] = "Neo4j__Uri", ["sourceKind"] = "json-path", ["valuePreview"] = "bolt://dev:7687" }
            });

            var definitions = await _repository.FindConfigDefinitionsAsync("Neo4j:Uri", projectContext);

            definitions.Should().HaveCount(2);
            definitions.Should().Contain(item =>
                item.RelationshipType == "DefinesConfig" &&
                item.FileNode.Id == baseFile.Id &&
                item.RawKey == "Neo4j:Uri");
            definitions.Should().Contain(item =>
                item.RelationshipType == "OverridesConfig" &&
                item.FileNode.Id == overrideFile.Id &&
                item.RawKey == "Neo4j__Uri");
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindConfigUsageAsync_WithTemporaryFixture_ReturnsReadAndBindEdges()
    {
        var projectContext = $"Integration.ConfigUsage.{Guid.NewGuid():N}";
        var keyNode = ConfigNode($"{projectContext}.Key", "Neo4j:Uri", CodeNodeType.ConfigurationKey, projectContext)
            with
            {
                Properties = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["canonicalKey"] = "Neo4j:Uri",
                    ["normalizedKey"] = "neo4j:uri"
                }
            };
        var reader = ConfigNode($"{projectContext}.Reader", "Read", CodeNodeType.Method, projectContext, $"src/{projectContext}/Reader.cs");
        var binder = ConfigNode($"{projectContext}.Binder", "Add", CodeNodeType.Method, projectContext, $"src/{projectContext}/Binder.cs");

        try
        {
            await _repository!.UpsertNodeAsync(keyNode);
            await _repository.UpsertNodeAsync(reader);
            await _repository.UpsertNodeAsync(binder);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = reader.Id,
                TargetId = keyNode.Id,
                Type = CodeEdgeType.ReadsConfig,
                Confidence = 0.95d,
                Properties = new Dictionary<string, string>
                {
                    ["rawKey"] = "Neo4j:Uri",
                    ["accessPattern"] = "indexer"
                }
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = binder.Id,
                TargetId = keyNode.Id,
                Type = CodeEdgeType.BindsConfig,
                Confidence = 0.9d,
                Properties = new Dictionary<string, string>
                {
                    ["rawKey"] = "Neo4j",
                    ["accessPattern"] = "Configure",
                    ["optionsType"] = "Neo4jOptions"
                }
            });

            var usage = await _repository.FindConfigUsageAsync("Neo4j:Uri", projectContext);

            usage.Should().HaveCount(2);
            usage.Should().Contain(item =>
                item.RelationshipType == "ReadsConfig" &&
                item.ConsumerNode.Id == reader.Id &&
                item.RawKey == "Neo4j:Uri");
            usage.Should().Contain(item =>
                item.RelationshipType == "BindsConfig" &&
                item.ConsumerNode.Id == binder.Id &&
                item.OptionsType == "Neo4jOptions");
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    private static CodeNode ConfigNode(
        string id,
        string name,
        CodeNodeType type,
        string projectContext,
        string? filePath = null) => new()
    {
        Id = id,
        Name = name,
        Type = type,
        ProjectContext = projectContext,
        FilePath = filePath
    };
}
