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
            projectContext: BaselineProjectContext,
            window: TimeSpan.FromDays(3650));

        results.Should().NotBeEmpty();
        results.Should().OnlyContain(result =>
            result.ChangedAt != default
            && !string.IsNullOrWhiteSpace(result.ChangeType));
    }

    [Fact]
    public async Task FindRecentlyChangedAsync_ExcludesOperationalDiagnosticNodes()
    {
        var projectContext = $"Integration.RecentCodeOnly.{Guid.NewGuid():N}";
        var method = new CodeNode
        {
            Id = $"{projectContext}::Method::Save",
            Name = "Save",
            Type = CodeNodeType.Method,
            ProjectContext = projectContext,
            FilePath = "src/Save.cs"
        };
        var diagnostic = new CodeNode
        {
            Id = $"{projectContext}::Diagnostic::CS0001",
            Name = "CS0001",
            Type = CodeNodeType.Diagnostic,
            ProjectContext = projectContext,
            FilePath = "src/Save.cs"
        };

        try
        {
            await _repository!.UpsertNodeAsync(method);
            await _repository.UpsertNodeAsync(diagnostic);

            var results = await _repository.FindRecentlyChangedAsync(projectContext, TimeSpan.FromMinutes(10));

            results.Select(result => result.Node.Id).Should().Contain(method.Id);
            results.Select(result => result.Node.Id).Should().NotContain(diagnostic.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
