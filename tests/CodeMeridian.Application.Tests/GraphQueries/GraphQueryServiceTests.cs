using CodeMeridian.Application.GraphQueries;
using CodeMeridian.Core.GraphQueries;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.Application.Tests.GraphQueries;

public sealed class GraphQueryServiceTests
{
    [Fact]
    public async Task ListLabelsAsync_RemovesBlankValuesDeduplicatesAndSorts()
    {
        var repository = Substitute.For<IGraphReadRepository>();
        repository.ListLabelsAsync(Arg.Any<CancellationToken>()).Returns(["Keyword", "", "CodeNode", "Keyword", "  "]);
        var sut = new GraphQueryService(repository);

        var result = await sut.ListLabelsAsync(CancellationToken.None);

        result.Should().Equal("CodeNode", "Keyword");
    }

    [Fact]
    public async Task ListRelationshipTypesAsync_RemovesBlankValuesDeduplicatesAndSorts()
    {
        var repository = Substitute.For<IGraphReadRepository>();
        repository.ListRelationshipTypesAsync(Arg.Any<CancellationToken>()).Returns(["CALLS", " ", "USES", "CALLS"]);
        var sut = new GraphQueryService(repository);

        var result = await sut.ListRelationshipTypesAsync(CancellationToken.None);

        result.Should().Equal("CALLS", "USES");
    }

    [Fact]
    public async Task QueryNodesAsync_WhenLimitExceedsMaximum_ClampsToConfiguredCap()
    {
        var repository = Substitute.For<IGraphReadRepository>();
        var sut = new GraphQueryService(repository);

        await sut.QueryNodesAsync(
            new GraphNodeFilter
            {
                Labels = ["CodeNode"]
            },
            new GraphSort("name", GraphSortDirection.Ascending),
            skip: 0,
            limit: 500,
            CancellationToken.None);

        await repository.Received(1).QueryNodesAsync(
            Arg.Any<GraphNodeFilter>(),
            Arg.Any<GraphSort>(),
            0,
            100,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueryNodesAsync_WithUnsupportedSortField_RejectsRequest()
    {
        var repository = Substitute.For<IGraphReadRepository>();
        var sut = new GraphQueryService(repository);

        var act = () => sut.QueryNodesAsync(
            new GraphNodeFilter(),
            new GraphSort("createdAt", GraphSortDirection.Descending),
            skip: 0,
            limit: 10,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*createdAt*");
    }

    [Fact]
    public async Task QueryNodesAsync_NormalizesFilterValuesBeforeCallingRepository()
    {
        var repository = Substitute.For<IGraphReadRepository>();
        repository.QueryNodesAsync(Arg.Any<GraphNodeFilter>(), Arg.Any<GraphSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var sut = new GraphQueryService(repository);

        await sut.QueryNodesAsync(
            new GraphNodeFilter
            {
                Labels = ["CodeNode", " ", "CodeNode"],
                PropertyEquals = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [" name "] = "NodeName",
                    [" "] = "ignored"
                },
                PropertyContains = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [" description "] = "graph",
                    [""] = "ignored"
                },
                NodeIds = ["node-1", " ", "node-1"],
                ProjectContext = " Demo.Project ",
                KeywordText = " graphql ",
                KeywordCategory = " analysis "
            },
            new GraphSort("name", GraphSortDirection.Ascending),
            skip: 2,
            limit: 15,
            CancellationToken.None);

        await repository.Received(1).QueryNodesAsync(
            Arg.Is<GraphNodeFilter>(filter =>
                filter.Labels.Count == 1
                && filter.Labels[0] == "CodeNode"
                && filter.PropertyEquals.Count == 1
                && filter.PropertyEquals.ContainsKey("name")
                && filter.PropertyEquals["name"] == "NodeName"
                && filter.PropertyContains.Count == 1
                && filter.PropertyContains.ContainsKey("description")
                && filter.PropertyContains["description"] == "graph"
                && filter.NodeIds.Count == 1
                && filter.NodeIds[0] == "node-1"
                && filter.ProjectContext == "Demo.Project"
                && filter.KeywordText == "graphql"
                && filter.KeywordCategory == "analysis"),
            Arg.Any<GraphSort>(),
            2,
            15,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(-1, 10, "skip")]
    [InlineData(0, 0, "limit")]
    public async Task QueryNodesAsync_InvalidPagination_RejectsRequest(int skip, int limit, string expectedParameter)
    {
        var repository = Substitute.For<IGraphReadRepository>();
        var sut = new GraphQueryService(repository);

        var act = () => sut.QueryNodesAsync(new GraphNodeFilter(), null, skip, limit, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .Where(exception => exception.ParamName == expectedParameter);
    }

    [Fact]
    public async Task GetNeighborsAsync_WhenDepthExceedsMaximum_ClampsDepth()
    {
        var repository = Substitute.For<IGraphReadRepository>();
        var sut = new GraphQueryService(repository);
        var expectedTypes = new[] { "HAS_KEYWORD" };

        await sut.GetNeighborsAsync(
            "node-1",
            expectedTypes,
            GraphDirection.Outgoing,
            depth: 8,
            limit: 20,
            CancellationToken.None);

        await repository.Received(1).GetNeighborsAsync(
            "node-1",
            Arg.Is<IReadOnlyCollection<string>>(types => types.SequenceEqual(expectedTypes)),
            GraphDirection.Outgoing,
            3,
            20,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetNeighborsAsync_TrimmedNodeIdAndRelationshipTypesAreNormalized()
    {
        var repository = Substitute.For<IGraphReadRepository>();
        repository.GetNeighborsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<GraphDirection>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var sut = new GraphQueryService(repository);

        await sut.GetNeighborsAsync(
            "  node-1  ",
            ["USES", " ", "USES", "CALLS"],
            GraphDirection.Incoming,
            depth: 1,
            limit: 10,
            CancellationToken.None);

        await repository.Received(1).GetNeighborsAsync(
            "node-1",
            Arg.Is<IReadOnlyCollection<string>>(types => types.Count == 2 && types.Contains("USES") && types.Contains("CALLS")),
            GraphDirection.Incoming,
            1,
            10,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetNodeAsync_WithBlankId_RejectsRequest()
    {
        var repository = Substitute.For<IGraphReadRepository>();
        var sut = new GraphQueryService(repository);

        var act = () => sut.GetNodeAsync("   ", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*nodeId*");
    }

    [Fact]
    public async Task QueryRelationshipsAsync_NormalizesFilterAndClampsLimit()
    {
        var repository = Substitute.For<IGraphReadRepository>();
        repository.QueryRelationshipsAsync(Arg.Any<GraphRelationshipFilter>(), Arg.Any<GraphSort>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var sut = new GraphQueryService(repository);

        await sut.QueryRelationshipsAsync(
            new GraphRelationshipFilter
            {
                RelationshipTypes = ["CALLS", " ", "CALLS"],
                PropertyEquals = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [" status "] = "active",
                    [" "] = "ignored"
                },
                PropertyContains = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [" note "] = "graph",
                    [""] = "ignored"
                },
                FromNodeIds = ["from-1", " ", "from-1"],
                ToNodeIds = ["to-1", "", "to-1"],
                ProjectContext = " Demo.Project "
            },
            new GraphSort("type", GraphSortDirection.Descending),
            skip: 1,
            limit: 500,
            CancellationToken.None);

        await repository.Received(1).QueryRelationshipsAsync(
            Arg.Is<GraphRelationshipFilter>(filter =>
                filter.RelationshipTypes.Count == 1
                && filter.RelationshipTypes[0] == "CALLS"
                && filter.PropertyEquals.Count == 1
                && filter.PropertyEquals.ContainsKey("status")
                && filter.PropertyEquals["status"] == "active"
                && filter.PropertyContains.Count == 1
                && filter.PropertyContains.ContainsKey("note")
                && filter.PropertyContains["note"] == "graph"
                && filter.FromNodeIds.Count == 1
                && filter.FromNodeIds[0] == "from-1"
                && filter.ToNodeIds.Count == 1
                && filter.ToNodeIds[0] == "to-1"
                && filter.ProjectContext == "Demo.Project"),
            Arg.Any<GraphSort>(),
            1,
            100,
            Arg.Any<CancellationToken>());
    }
}
