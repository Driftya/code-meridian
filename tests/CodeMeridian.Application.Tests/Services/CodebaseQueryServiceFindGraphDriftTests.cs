using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindGraphDriftTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindGraphDriftAsync_WhenProjectContextHasTypo_SuggestsClosestProject()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .GetProjectContextsAsync("code3meridian", Arg.Any<CancellationToken>())
            .Returns(["CodeMeridian"]);

        var result = await sut.FindGraphDriftAsync("code3meridian");

        result.Should().Contain("No graph nodes found in 'code3meridian'");
        result.Should().Contain("Did you mean 'CodeMeridian'?");
        result.Should().Contain("Run the indexer");
    }

    [Fact]
    public async Task FindGraphDriftAsync_WhenPrefilterFindsNoProjects_FallsBackToAllProjects()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .GetProjectContextsAsync("xode-meridian", Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .GetProjectContextsAsync(null, Arg.Any<CancellationToken>())
            .Returns(["CodeMeridian"]);

        var result = await sut.FindGraphDriftAsync("xode-meridian");

        result.Should().Contain("Did you mean 'CodeMeridian'?");
    }

    [Fact]
    public async Task FindGraphDriftAsync_WithIncompleteIndexedMetadata_ReturnsRecommendation()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("n1", "ServiceWithoutPath", CodeNodeType.Class, project: "CodeMeridian"),
                Node("n2", "MethodWithoutLine", CodeNodeType.Method, "src/Service.cs", project: "CodeMeridian", updatedAt: DateTimeOffset.UtcNow),
                Node("n3", "CodeMeridian.Services", CodeNodeType.Namespace, "src/Service.cs", 1, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow)
            ]);

        var result = await sut.FindGraphDriftAsync("CodeMeridian");

        result.Should().Contain("## Graph Drift");
        result.Should().Contain("Source verification");
        result.Should().Contain("Missing file metadata");
        result.Should().Contain("Missing source hashes");
        result.Should().Contain("ServiceWithoutPath");
        result.Should().NotContain("CodeMeridian.Services");
        result.Should().Contain("codemeridian index");
    }

    [Fact]
    public async Task FindGraphDriftAsync_IgnoresStructuralAndConfigurationMetadataThatIsNotRequired()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("cfg-key", "Embedding:Enabled", CodeNodeType.ConfigurationKey, project: "CodeMeridian", updatedAt: DateTimeOffset.UtcNow),
                Node("cfg-entry", "Embedding__Enabled", CodeNodeType.ConfigurationEntry, ".env", project: "CodeMeridian", updatedAt: DateTimeOffset.UtcNow),
                Node("api", "POST /nodes", CodeNodeType.ApiEndpoint, project: "CodeMeridian", updatedAt: DateTimeOffset.UtcNow),
                Node("cfg-file", ".env", CodeNodeType.ConfigurationFile, ".env", project: "CodeMeridian", updatedAt: DateTimeOffset.UtcNow, sourceHash: "env-hash")
            ]);

        var result = await sut.FindGraphDriftAsync("CodeMeridian");

        result.Should().Contain("## Graph Drift - CodeMeridian");
        result.Should().Contain("Relationship completeness:** Unknown");
        result.Should().Contain("no index-run relationship statistics are available");
    }
}

