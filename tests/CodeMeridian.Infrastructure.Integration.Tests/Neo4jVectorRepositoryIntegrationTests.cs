using CodeMeridian.Core.Knowledge;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

public sealed class Neo4jVectorRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly Neo4jOptions _options;
    private Neo4jVectorRepository? _repository;

    public Neo4jVectorRepositoryIntegrationTests()
    {
        _options = TestEnvironment.TryGetNeo4jOptions()
            ?? throw new InvalidOperationException("Neo4j connection details were not found in environment or repo .env.");
    }

    public async Task InitializeAsync()
    {
        _repository = new Neo4jVectorRepository(Options.Create(_options), NullLogger<Neo4jVectorRepository>.Instance);
        await _repository.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_repository is not null)
            await _repository.DisposeAsync();
    }

    [Fact]
    public async Task SearchByTextAsync_UsesIndexedDocsFromTheRepo()
    {
        var results = await _repository!.SearchByTextAsync("CodeMeridian", projectContext: null, topK: 20);

        results.Should().NotBeEmpty();
        var hasExpectedDoc = results.Any(doc =>
            doc.Content.Contains("CodeMeridian", StringComparison.OrdinalIgnoreCase)
            || (doc.Source is not null && doc.Source.Contains("README", StringComparison.OrdinalIgnoreCase))
            || (doc.Source is not null && doc.Source.Contains("TODO", StringComparison.OrdinalIgnoreCase)));

        hasExpectedDoc.Should().BeTrue();
    }

    [Fact]
    public async Task ListAsync_ReturnsProjectDocuments()
    {
        var results = await _repository!.ListAsync(projectContext: null, limit: 50);

        results.Should().NotBeEmpty();
    }
}
