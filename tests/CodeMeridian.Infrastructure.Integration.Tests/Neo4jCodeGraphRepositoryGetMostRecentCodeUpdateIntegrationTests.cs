using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryGetMostRecentCodeUpdateIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task GetMostRecentCodeUpdateAsync_ForRepo_ReturnsTimestamp()
    {
        var updatedAt = await _repository!.GetMostRecentCodeUpdateAsync();

        updatedAt.Should().NotBeNull();
        updatedAt!.Value.Should().BeAfter(DateTimeOffset.UnixEpoch);
    }


}
