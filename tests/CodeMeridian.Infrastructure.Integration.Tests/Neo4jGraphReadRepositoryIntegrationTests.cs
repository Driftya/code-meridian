using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.GraphQueries;
using CodeMeridian.Core.KeywordGraph;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using CodeMeridian.Infrastructure.GraphQueries;
using CodeMeridian.Infrastructure.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

public sealed class Neo4jGraphReadRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly Neo4jOptions _options;
    private Neo4jCodeGraphRepository? _codeGraphRepository;
    private Neo4jKeywordGraphRepository? _keywordGraphRepository;
    private Neo4jVectorRepository? _vectorRepository;
    private Neo4jGraphReadRepository? _sut;

    public Neo4jGraphReadRepositoryIntegrationTests()
    {
        _options = TestEnvironment.TryGetNeo4jOptions()
            ?? throw new InvalidOperationException("Neo4j connection details were not found in environment or repo .env.");
    }

    public async Task InitializeAsync()
    {
        var options = Options.Create(_options);
        _codeGraphRepository = new Neo4jCodeGraphRepository(options, NullLogger<Neo4jCodeGraphRepository>.Instance);
        _keywordGraphRepository = new Neo4jKeywordGraphRepository(options, NullLogger<Neo4jKeywordGraphRepository>.Instance);
        _vectorRepository = new Neo4jVectorRepository(options, NullLogger<Neo4jVectorRepository>.Instance);
        _sut = new Neo4jGraphReadRepository(options, NullLogger<Neo4jGraphReadRepository>.Instance);

        await _codeGraphRepository.InitializeAsync();
        await _keywordGraphRepository.InitializeAsync();
        await _vectorRepository.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_sut is not null)
            await _sut.DisposeAsync();

        if (_vectorRepository is not null)
            await _vectorRepository.DisposeAsync();

        if (_keywordGraphRepository is not null)
            await _keywordGraphRepository.DisposeAsync();

        if (_codeGraphRepository is not null)
            await _codeGraphRepository.DisposeAsync();
    }

    [Fact]
    public async Task QueryNodesAsync_WithKeywordTextFilter_ReturnsKeywordNodes()
    {
        var projectContext = $"Integration.GraphRead.Keyword.{Guid.NewGuid():N}";
        var sourceNode = CreateCodeNode(
            id: $"{projectContext}.Source",
            name: "GraphQueryService",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/GraphQueryService.cs");

        try
        {
            await _codeGraphRepository!.UpsertNodeAsync(sourceNode);
            await _keywordGraphRepository!.ReplaceKeywordsAsync(new ReplaceKeywordRelationshipsCommand
            {
                SourceNodeId = sourceNode.Id,
                KeywordTextChecksum = "checksum",
                EnrichmentVersion = 1,
                Keywords =
                [
                    new ExtractedKeyword
                    {
                        Value = "GraphQL",
                        NormalizedValue = "graphql",
                        Count = 2,
                        Weight = 1.5,
                        Sources = ["name"]
                    }
                ]
            });
            await _keywordGraphRepository.RecalculateKeywordStatisticsAsync(projectContext);

            var nodes = await _sut!.QueryNodesAsync(
                new GraphNodeFilter
                {
                    Labels = ["Keyword"],
                    ProjectContext = projectContext,
                    KeywordText = "graph"
                },
                new GraphSort("name", GraphSortDirection.Ascending),
                0,
                10);

            nodes.Should().ContainSingle(node =>
                node.PrimaryLabel == "Keyword"
                && node.Properties.Any(property => property.Key == "normalizedValue" && property.Value == "graphql"));
        }
        finally
        {
            await _codeGraphRepository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task GetNodeAsync_ForKnowledgeDocument_ReturnsKnowledgeDocumentShape()
    {
        var projectContext = $"Integration.GraphRead.Document.{Guid.NewGuid():N}";
        var document = new KnowledgeDocument
        {
            Id = $"{projectContext}.doc",
            Content = "GraphQL query design notes",
            Source = "docs/graphql.md",
            ProjectContext = projectContext,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["kind"] = "design-note"
            }
        };

        try
        {
            await _vectorRepository!.UpsertAsync(document);

            var node = await _sut!.GetNodeAsync(document.Id);

            node.Should().NotBeNull();
            node!.Labels.Should().Contain("KnowledgeDocument");
            node.PrimaryLabel.Should().Be("KnowledgeDocument");
            node.Properties.Should().Contain(property => property.Key == "content" && property.Value == "GraphQL query design notes");
            node.Properties.Should().Contain(property => property.Key == "metadataKind" && property.Value == "design-note");
        }
        finally
        {
            await _vectorRepository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task GetNeighborsAsync_ForKeywordSource_ReturnsHasKeywordRelationship()
    {
        var projectContext = $"Integration.GraphRead.Neighbors.{Guid.NewGuid():N}";
        var sourceNode = CreateCodeNode(
            id: $"{projectContext}.Source",
            name: "HotChocolatePlan",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/HotChocolatePlan.cs");

        try
        {
            await _codeGraphRepository!.UpsertNodeAsync(sourceNode);
            await _keywordGraphRepository!.ReplaceKeywordsAsync(new ReplaceKeywordRelationshipsCommand
            {
                SourceNodeId = sourceNode.Id,
                KeywordTextChecksum = "checksum",
                EnrichmentVersion = 1,
                Keywords =
                [
                    new ExtractedKeyword
                    {
                        Value = "Neo4j",
                        NormalizedValue = "neo4j",
                        Count = 1,
                        Weight = 1.0,
                        Sources = ["summary"]
                    }
                ]
            });
            await _keywordGraphRepository.RecalculateKeywordStatisticsAsync(projectContext);

            var neighbors = await _sut!.GetNeighborsAsync(
                sourceNode.Id,
                ["HAS_KEYWORD"],
                GraphDirection.Outgoing,
                1,
                10);

            neighbors.Should().ContainSingle(neighbor =>
                neighbor.Distance == 1
                && neighbor.Relationship.Type == "HAS_KEYWORD"
                && neighbor.Node.PrimaryLabel == "Keyword");
        }
        finally
        {
            await _codeGraphRepository!.DeleteProjectAsync(projectContext);
        }
    }

    private static CodeNode CreateCodeNode(
        string id,
        string name,
        CodeNodeType type,
        string projectContext,
        string filePath)
    {
        return new CodeNode
        {
            Id = id,
            Name = name,
            Type = type,
            ProjectContext = projectContext,
            FilePath = filePath
        };
    }
}
