using CodeMeridian.Application.GraphQueries;
using CodeMeridian.Core.GraphQueries;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.Application.Tests.GraphQueries;

public sealed class GraphQueryServiceTests
{
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
    public async Task GetNodeAsync_WithBlankId_RejectsRequest()
    {
        var repository = Substitute.For<IGraphReadRepository>();
        var sut = new GraphQueryService(repository);

        var act = () => sut.GetNodeAsync("   ", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*nodeId*");
    }
}
