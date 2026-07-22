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
        var projectContext = $"Integration.Documents.Search.{Guid.NewGuid():N}";
        var document = new KnowledgeDocument
        {
            Id = $"{projectContext}.Doc",
            Content = "CodeMeridian isolated full-text integration fixture.",
            Source = "docs/search-fixture.md",
            ProjectContext = projectContext
        };

        try
        {
            await _repository!.UpsertAsync(document);

            var results = await _repository.SearchByTextAsync("CodeMeridian", projectContext, topK: 20);

            results.Should().ContainSingle(doc => doc.Id == document.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task ListAsync_ReturnsProjectDocuments()
    {
        var projectContext = $"Integration.Documents.List.{Guid.NewGuid():N}";
        var document = new KnowledgeDocument
        {
            Id = $"{projectContext}.Doc",
            Content = "Isolated integration document for list coverage.",
            Source = "docs/list-fixture.md",
            ProjectContext = projectContext
        };

        try
        {
            await _repository!.UpsertAsync(document);

            var results = await _repository.ListAsync(projectContext, limit: 50);

            results.Should().ContainSingle(doc => doc.Id == document.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task CountAsync_WithTemporaryDocumentFixture_ReturnsMatchingCount()
    {
        var projectContext = $"Integration.Documents.{Guid.NewGuid():N}";
        var document = new KnowledgeDocument
        {
            Id = $"{projectContext}.Doc",
            Content = "Temporary integration document for count coverage.",
            Source = "docs/integration.md",
            ProjectContext = projectContext,
            Metadata = new Dictionary<string, string>
            {
                ["kind"] = "integration"
            }
        };

        try
        {
            await _repository!.UpsertAsync(document);

            var count = await _repository.CountAsync(projectContext);

            count.Should().BeGreaterOrEqualTo(1);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task ProjectScopedDocumentQueries_AreCaseInsensitive()
    {
        var projectContext = $"Integration.Documents.Case.{Guid.NewGuid():N}";
        var document = new KnowledgeDocument
        {
            Id = $"{projectContext}.Doc",
            Content = "Temporary integration document for case-insensitive project lookup.",
            Source = "docs/case.md",
            ProjectContext = projectContext
        };

        try
        {
            await _repository!.UpsertAsync(document);

            var lowerCaseProject = projectContext.ToLowerInvariant();
            var listed = await _repository.ListAsync(lowerCaseProject, limit: 10);
            var count = await _repository.CountAsync(lowerCaseProject);

            listed.Should().Contain(doc => doc.Id == document.Id);
            count.Should().BeGreaterOrEqualTo(1);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }
}
