using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceConfigurationTests
{
    [Fact]
    public async Task FindConfigDefinitionsAsync_WhenEmpty_ReturnsGuidance()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        graph.FindConfigDefinitionsAsync("Neo4j:Uri", "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([]);
        var sut = new CodebaseQueryService(graph, vector);

        var result = await sut.FindConfigDefinitionsAsync("Neo4j:Uri", "CodeMeridian");

        result.Should().Contain("No configuration definitions found");
        result.Should().Contain("codemeridian config rebuild");
    }

    [Fact]
    public async Task FindConfigDefinitionsAsync_WithResults_ReturnsMarkdownTable()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        graph.FindConfigDefinitionsAsync("Neo4j:Uri", "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([
                new ConfigurationDefinition
                {
                    FileNode = Node("file-1", "appsettings.json", CodeNodeType.ConfigurationFile, "appsettings.json"),
                    EntryNode = Node("entry-1", "Neo4j:Uri", CodeNodeType.ConfigurationEntry, "appsettings.json"),
                    KeyNode = Node("key-1", "Neo4j:Uri", CodeNodeType.ConfigurationKey),
                    RelationshipType = "DefinesConfig",
                    RawKey = "Neo4j:Uri",
                    SourceKind = "json-path",
                    ValuePreview = "bolt://localhost:7687"
                }
            ]);
        var sut = new CodebaseQueryService(graph, vector);

        var result = await sut.FindConfigDefinitionsAsync("Neo4j:Uri", "CodeMeridian");

        result.Should().Contain("## Config Definitions");
        result.Should().Contain("DefinesConfig");
        result.Should().Contain("appsettings.json");
        result.Should().Contain("bolt://localhost:7687");
    }

    [Fact]
    public async Task FindConfigUsageAsync_WhenEmpty_ReturnsGuidance()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        graph.FindConfigUsageAsync("Neo4j:Uri", "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([]);
        var sut = new CodebaseQueryService(graph, vector);

        var result = await sut.FindConfigUsageAsync("Neo4j:Uri", "CodeMeridian");

        result.Should().Contain("No configuration usage found");
        result.Should().Contain("ReadsConfig");
    }

    [Fact]
    public async Task FindConfigUsageAsync_WithResults_ReturnsMarkdownTable()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        graph.FindConfigUsageAsync("Neo4j:Uri", "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([
                new ConfigurationUsage
                {
                    ConsumerNode = Node("method-1", "Add(IServiceCollection,IConfiguration)", CodeNodeType.Method, "src/Bootstrap.cs"),
                    KeyNode = Node("key-1", "Neo4j:Uri", CodeNodeType.ConfigurationKey),
                    RelationshipType = "BindsConfig",
                    RawKey = "Neo4j",
                    AccessPattern = "Configure",
                    OptionsType = "Neo4jOptions",
                    Confidence = 0.9d
                }
            ]);
        var sut = new CodebaseQueryService(graph, vector);

        var result = await sut.FindConfigUsageAsync("Neo4j:Uri", "CodeMeridian");

        result.Should().Contain("## Config Usage");
        result.Should().Contain("BindsConfig");
        result.Should().Contain("Neo4jOptions");
        result.Should().Contain("0.9");
    }

    private static CodeNode Node(string id, string name, CodeNodeType type, string? filePath = null) => new()
    {
        Id = id,
        Name = name,
        Type = type,
        FilePath = filePath
    };
}
