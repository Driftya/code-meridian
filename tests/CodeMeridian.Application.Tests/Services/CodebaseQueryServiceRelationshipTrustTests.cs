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

    [Fact]
    public async Task CheckGraphFreshnessAsync_WithOnlyExternalV2Outcomes_KeepsHighTrust()
    {
        var (sut, graph) = Build();
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.TypeFilter == null),
                Arg.Any<CancellationToken>())
            .Returns([SourceNode()]);
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.TypeFilter == CodeNodeType.IndexRun),
                Arg.Any<CancellationToken>())
            .Returns([V2IndexRun(external: 10856, unresolvedLocal: 0, indeterminate: 0)]);

        var result = await sut.CheckGraphFreshnessAsync(projectContext: "Project");

        result.Should().Contain("**Relationship completeness:** High");
        result.Should().Contain("classified 10856 relationship(s) as external or outside the indexed scope");
        result.Should().NotContain("Relationship remediation");
    }

    [Fact]
    public async Task FindHotspotsAsync_WithV2LocalAndIndeterminateOutcomes_DegradesTrustAndShowsSamples()
    {
        var (sut, graph) = Build();
        graph.FindHotspotsAsync("Project", 15, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<(CodeNode Node, int FanIn)>());
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.TypeFilter == CodeNodeType.Diagnostic),
                Arg.Any<CancellationToken>())
            .Returns([V2IndexRun(external: 4, unresolvedLocal: 2, indeterminate: 1, compatible: true)]);

        var result = await sut.FindHotspotsAsync("Project");

        result.Should().Contain("Relationship completeness is low");
        result.Should().Contain("2 unresolved local relationship(s)");
        result.Should().Contain("top call reasons: unresolved_local:missing_local_target=2");
        result.Should().Contain("5.4% of calls");
        result.Should().Contain("src/Service.cs:12");
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

    private static CodeNode V2IndexRun(
        int external,
        int unresolvedLocal,
        int indeterminate,
        bool compatible = false) => new()
    {
        Id = "Project::IndexRun::v2",
        Name = "full C# index run",
        Type = compatible ? CodeNodeType.Diagnostic : CodeNodeType.IndexRun,
        ProjectContext = "Project",
        UpdatedAt = DateTimeOffset.Parse("2026-07-22T10:00:00Z"),
        LastIndexedAt = DateTimeOffset.Parse("2026-07-22T10:00:00Z"),
        Properties = new Dictionary<string, string>
        {
            ["externalKind"] = "IndexRun",
            ["relationshipHealthSchemaVersion"] = "2",
            ["language"] = "CSharp",
            ["resolutionScope"] = "project",
            ["mode"] = "full",
            ["scannedFileCount"] = "40",
            ["ingestedFileCount"] = "40",
            ["attemptedCallEdges"] = (30 + external + unresolvedLocal + indeterminate).ToString(),
            ["resolvedCallEdges"] = "30",
            ["attemptedReferenceEdges"] = "0",
            ["resolvedReferenceEdges"] = "0",
            ["externalOrUnindexedRelationshipCount"] = external.ToString(),
            ["unresolvedLocalRelationshipCount"] = unresolvedLocal.ToString(),
            ["indeterminateRelationshipCount"] = indeterminate.ToString(),
            ["callRelationshipOutcomes"] = System.Text.Json.JsonSerializer.Serialize(new
            {
                Reasons = new Dictionary<string, int>
                {
                    ["unresolved_local:missing_local_target"] = unresolvedLocal,
                    ["indeterminate:unknown_member_receiver"] = indeterminate
                }
            }),
            ["relationshipFailureSamples"] = """[{"FilePath":"src/Service.cs","LineNumber":12,"Reason":"missing_local_target"}]""",
            ["usedFullResolutionCatalog"] = "true"
        }
    };
}
