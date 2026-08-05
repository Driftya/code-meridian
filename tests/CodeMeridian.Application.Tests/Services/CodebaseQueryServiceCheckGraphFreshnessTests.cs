using System.Text.Json;
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
    public async Task CheckGraphFreshnessResultAsync_ReturnsBoundedTypedFactsWithoutSourceBodies()
    {
        var (sut, graph) = Build();
        var nodes = Enumerable.Range(1, 205)
            .Select(index => Node(
                $"n{index}",
                $"Node{index}",
                index % 2 == 0 ? CodeNodeType.Method : CodeNodeType.Class,
                $"src/Node{index}.cs",
                index,
                "CodeMeridian",
                updatedAt: index % 3 == 0 ? null : DateTimeOffset.UtcNow,
                lineCount: index % 5 == 0 ? null : 12,
                sourceHash: index % 7 == 0 ? null : "hash",
                sourceSnippet: "private const string SecretBody = \"must-not-leak\";"))
            .ToArray();
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns(nodes);

        var result = await sut.CheckGraphFreshnessResultAsync(projectContext: "CodeMeridian", limit: 200);
        var json = JsonSerializer.Serialize(result);

        result.ContractVersion.Should().Be("1.0");
        result.NodesFound.Should().BeTrue();
        result.Findings.Should().HaveCount(200);
        result.FindingsTruncated.Should().BeTrue();
        result.HighConfidenceCount.Should().BeGreaterThan(0);
        result.MediumConfidenceCount.Should().BeGreaterThan(0);
        result.LowConfidenceCount.Should().BeGreaterThan(0);
        json.Should().NotContain("must-not-leak");
        json.Should().NotContain("SourceSnippet");
        json.Should().NotContain("Properties");
    }

    [Fact]
    public async Task CheckGraphFreshnessResultAsync_WhenNoNodes_ReturnsTypedEmptyResult()
    {
        var (sut, graph) = Build();
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        graph.GetProjectContextsAsync("missing", Arg.Any<CancellationToken>())
            .Returns(["CodeMeridian"]);

        var result = await sut.CheckGraphFreshnessResultAsync(projectContext: "missing");

        result.NodesFound.Should().BeFalse();
        result.Findings.Should().BeEmpty();
        result.RelationshipCompleteness.Should().BeNull();
        result.ProjectHint.Should().Contain("CodeMeridian");
        result.ToMarkdown().Should().Contain("No graph nodes found");
    }

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
