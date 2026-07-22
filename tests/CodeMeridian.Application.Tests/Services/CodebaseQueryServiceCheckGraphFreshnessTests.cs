using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceCheckGraphFreshnessTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task CheckGraphFreshnessAsync_ReturnsConfidenceSignals()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("n1", "Roadmap", CodeNodeType.File, "TODO.md", 1, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow, lineCount: 120, sourceHash: "abc123"),
                Node("n2", "Incomplete", CodeNodeType.Class, "src/File.cs", project: "CodeMeridian", updatedAt: DateTimeOffset.UtcNow)
            ]);

        var result = await sut.CheckGraphFreshnessAsync(projectContext: "CodeMeridian");

        result.Should().Contain("## Graph Freshness");
        result.Should().Contain("Trust summary");
        result.Should().Contain("High");
        result.Should().Contain("Medium");
        result.Should().Contain("Source verification");
        result.Should().Contain("checksum indexed");
        result.Should().Contain("missing source hash");
    }

    [Fact]
    public async Task CheckGraphFreshnessAsync_TreatsConfigurationNodesAsExpectedMetadataShapes()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("cfg-key", "Embedding:Enabled", CodeNodeType.ConfigurationKey, project: "CodeMeridian", updatedAt: DateTimeOffset.UtcNow),
                Node("cfg-entry", "Embedding__Enabled", CodeNodeType.ConfigurationEntry, ".env", project: "CodeMeridian", updatedAt: DateTimeOffset.UtcNow),
                Node("cfg-file", ".env", CodeNodeType.ConfigurationFile, ".env", project: "CodeMeridian", updatedAt: DateTimeOffset.UtcNow, sourceHash: "env-hash")
            ]);

        var result = await sut.CheckGraphFreshnessAsync(projectContext: "CodeMeridian");

        result.Should().Contain("## Graph Freshness");
        result.Should().Contain("3 High, 0 Medium, 0 Low confidence");
        result.Should().Contain("not required");
        result.Should().Contain("structural node with content-update metadata");
        result.Should().Contain("indexer supplied the metadata expected for this node type");
    }

    [Fact]
    public async Task CheckGraphFreshnessAsync_WhenProjectContextHasNoNodes_SuggestsClosestProject()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .GetProjectContextsAsync("code3meridian", Arg.Any<CancellationToken>())
            .Returns(["CodeMeridian"]);

        var result = await sut.CheckGraphFreshnessAsync(projectContext: "code3meridian");

        result.Should().Contain("No graph nodes found in 'code3meridian'");
        result.Should().Contain("Did you mean 'CodeMeridian'?");
    }

    [Fact]
    public async Task CheckGraphFreshnessAsync_WithCanonicalizableProjectContext_UsesCanonicalProject()
    {
        var (sut, graph) = Build();
        var target = Node(
            "fresh",
            "Fresh",
            CodeNodeType.Class,
            "src/Fresh.cs",
            1,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 12,
            sourceHash: "abc");

        graph.GetProjectContextsAsync("code-meridian", Arg.Any<CancellationToken>())
            .Returns(["CodeMeridian"]);
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q => q.ProjectContext == "CodeMeridian"),
                Arg.Any<CancellationToken>())
            .Returns([target]);

        var result = await sut.CheckGraphFreshnessAsync(projectContext: "code-meridian");

        result.Should().Contain("## Graph Freshness - CodeMeridian");
        result.Should().Contain("Fresh");
        result.Should().NotContain("No graph nodes found");
    }


}

