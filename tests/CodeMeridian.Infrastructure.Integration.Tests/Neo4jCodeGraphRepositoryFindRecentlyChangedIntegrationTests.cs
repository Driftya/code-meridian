using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryFindRecentlyChangedIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task FindRecentlyChangedAsync_ForRepo_ReturnsRecentNodes()
    {
        var results = await _repository!.FindRecentlyChangedAsync(
            projectContext: null,
            window: TimeSpan.FromDays(3650));

        results.Should().NotBeEmpty();
        results.Should().OnlyContain(result =>
            result.ChangedAt != default
            && !string.IsNullOrWhiteSpace(result.ChangeType));
    }


}
