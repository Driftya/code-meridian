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

    [Fact]
    public async Task FindGraphDriftAsync_WithCompleteNodeMetadataAndMediumRelationshipTrust_DoesNotRecommendClear()
    {
        var (sut, graph) = Build();
        var now = DateTimeOffset.UtcNow;
        var source = Node(
            "service",
            "Service",
            CodeNodeType.Class,
            "src/Service.cs",
            1,
            "CodeMeridian",
            updatedAt: now,
            lineCount: 20,
            sourceHash: "service-hash");
        var indexRun = Node("run", "full C# index run", CodeNodeType.IndexRun, project: "CodeMeridian", updatedAt: now) with
        {
            Properties = new Dictionary<string, string>
            {
                ["mode"] = "full",
                ["usedFullResolutionCatalog"] = "true",
                ["attemptedCallEdges"] = "10",
                ["resolvedCallEdges"] = "9",
                ["attemptedReferenceEdges"] = "0",
                ["resolvedReferenceEdges"] = "0"
            }
        };
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>()).Returns([source, indexRun]);

        var result = await sut.FindGraphDriftAsync("CodeMeridian");

        result.Should().Contain("Relationship remediation");
        result.Should().Contain("without `--clear`");
        result.Should().NotContain("one-time `--clear` rebuild");
    }

    [Fact]
    public async Task FindGraphDriftAsync_StoredSourceRoleOnTestPath_ReportsRoleConflict()
    {
        var (sut, graph) = Build();
        var now = DateTimeOffset.UtcNow;
        var testHelper = Node(
            "test-helper",
            "TempProjectHarness.writeFile",
            CodeNodeType.Method,
            "tests/walker-test-helpers.ts",
            1,
            "CodeMeridian",
            updatedAt: now,
            lineCount: 10,
            sourceHash: "test-helper-hash") with
        {
            FileRole = IndexedFileRole.Source
        };
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>()).Returns([testHelper]);

        var result = await sut.FindGraphDriftAsync("CodeMeridian");

        result.Should().Contain("Conflicting test file roles (1)");
        result.Should().Contain("stored as Source, path classifies as Test");
        result.Should().Contain("TempProjectHarness.writeFile");
    }
}
