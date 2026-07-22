using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceRelationshipTrustTests
{
    [Fact]
    public async Task CheckGraphFreshnessAsync_WithCompleteFullAndIncrementalRuns_ReportsHighRelationshipTrust()
    {
        var (sut, graph) = Build();
        var fullAt = DateTimeOffset.Parse("2026-07-20T10:00:00Z");
        var incrementalAt = DateTimeOffset.Parse("2026-07-21T10:00:00Z");
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.TypeFilter == null),
                Arg.Any<CancellationToken>())
            .Returns([SourceNode()]);
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.TypeFilter == CodeNodeType.IndexRun),
                Arg.Any<CancellationToken>())
            .Returns([
                IndexRun("full", fullAt, scanned: 40, ingested: 40, attempted: 30, resolved: 30),
                IndexRun("incremental", incrementalAt, scanned: 40, ingested: 4, attempted: 30, resolved: 30)
            ]);

        var result = await sut.CheckGraphFreshnessAsync(projectContext: "Project");

        result.Should().Contain("**Relationship completeness:** High");
        result.Should().Contain("**Last full index:** 2026-07-20 10:00:00Z");
        result.Should().Contain("**Last incremental index:** 2026-07-21 10:00:00Z");
        result.Should().NotContain("IndexRun");
    }

    [Fact]
    public async Task FindHotspotsAsync_WithUnresolvedRelationships_WarnsThatEmptyResultsAreNotSafe()
    {
        var (sut, graph) = Build();
        graph.FindHotspotsAsync("Project", 15, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<(CodeNode Node, int FanIn)>());
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.TypeFilter == CodeNodeType.Diagnostic),
                Arg.Any<CancellationToken>())
            .Returns([
                IndexRun("incremental", DateTimeOffset.UtcNow, scanned: 50, ingested: 2, attempted: 20, resolved: 12, compatible: true)
            ]);

        var result = await sut.FindHotspotsAsync("Project");

        result.Should().Contain("Relationship completeness is medium");
        result.Should().Contain("empty relationship result is not proof that a change is safe");
    }

    [Fact]
    public async Task FindHotspotsAsync_WithoutIndexRunMetadata_ReportsUnknownTrust()
    {
        var (sut, graph) = Build();
        graph.FindHotspotsAsync("Project", 15, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<(CodeNode Node, int FanIn)>());

        var result = await sut.FindHotspotsAsync("Project");

        result.Should().Contain("Relationship completeness is unknown");
    }

    private static (CodebaseQueryService Sut, ICodeGraphRepository Graph) Build()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vectors = Substitute.For<IVectorRepository>();
        return (new CodebaseQueryService(graph, vectors), graph);
    }

    private static CodeNode SourceNode() => new()
    {
        Id = "Project::Class::Sample.Service",
        Name = "Service",
        Type = CodeNodeType.Class,
        FilePath = "src/Service.cs",
        LineNumber = 1,
        LineCount = 20,
        ProjectContext = "Project",
        UpdatedAt = DateTimeOffset.Parse("2026-07-21T10:00:00Z"),
        LastIndexedAt = DateTimeOffset.Parse("2026-07-21T10:00:00Z"),
        SourceHash = "hash",
        FileRole = IndexedFileRole.Source
    };

    private static CodeNode IndexRun(
        string mode,
        DateTimeOffset indexedAt,
        int scanned,
        int ingested,
        int attempted,
        int resolved,
        bool compatible = false) => new()
    {
        Id = $"Project::IndexRun::{mode}",
        Name = $"{mode} C# index run",
        Type = compatible ? CodeNodeType.Diagnostic : CodeNodeType.IndexRun,
        ProjectContext = "Project",
        UpdatedAt = indexedAt,
        LastIndexedAt = indexedAt,
        Properties = new Dictionary<string, string>
        {
            ["externalKind"] = "IndexRun",
            ["mode"] = mode,
            ["scannedFileCount"] = scanned.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ingestedFileCount"] = ingested.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["attemptedCallEdges"] = attempted.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["resolvedCallEdges"] = resolved.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["attemptedReferenceEdges"] = "0",
            ["resolvedReferenceEdges"] = "0",
            ["usedFullResolutionCatalog"] = "true"
        }
    };
}
