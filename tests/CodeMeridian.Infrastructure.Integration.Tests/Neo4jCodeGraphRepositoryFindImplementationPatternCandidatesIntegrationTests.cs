using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryFindImplementationPatternCandidatesIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task FindImplementationPatternCandidatesAsync_ExcludesTestsAndLowSimilarityMatches()
    {
        var projectContext = $"Integration.Patterns.{Guid.NewGuid():N}";
        var endpoint = CreateNode(
            id: $"{projectContext}.AcceptInviteEndpoint",
            name: "POST /api/invites/accept",
            type: CodeNodeType.ApiEndpoint,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/AcceptInviteEndpoint.cs",
            namespaceName: $"{projectContext}.Api.Invites",
            embedding: [1f, 0f, 0f, 0f],
            fileRole: IndexedFileRole.Source);
        var service = CreateNode(
            id: $"{projectContext}.AcceptInviteService",
            name: "AcceptInviteService",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/AcceptInviteService.cs",
            namespaceName: $"{projectContext}.Application.Invites",
            embedding: [0.98f, 0.02f, 0f, 0f],
            fileRole: IndexedFileRole.Source);
        var repository = CreateNode(
            id: $"{projectContext}.InviteRepository",
            name: "InviteRepository",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/InviteRepository.cs",
            namespaceName: $"{projectContext}.Infrastructure.Invites",
            embedding: [0.95f, 0.05f, 0f, 0f],
            fileRole: IndexedFileRole.Source);
        var testNode = CreateNode(
            id: $"{projectContext}.AcceptInviteServiceTests",
            name: "AcceptInviteServiceTests",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"tests/{projectContext}/AcceptInviteServiceTests.cs",
            namespaceName: $"{projectContext}.Tests.Invites",
            embedding: [0.99f, 0.01f, 0f, 0f],
            fileRole: IndexedFileRole.Test);
        var unrelated = CreateNode(
            id: $"{projectContext}.Unrelated",
            name: "UnrelatedBatchJob",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/UnrelatedBatchJob.cs",
            namespaceName: $"{projectContext}.Jobs",
            embedding: [-1f, 0f, 0f, 0f],
            fileRole: IndexedFileRole.Source);

        try
        {
            await _repository!.UpsertNodeAsync(endpoint);
            await _repository.UpsertNodeAsync(service);
            await _repository.UpsertNodeAsync(repository);
            await _repository.UpsertNodeAsync(testNode);
            await _repository.UpsertNodeAsync(unrelated);

            var results = await _repository.FindImplementationPatternCandidatesAsync(
                [1f, 0f, 0f, 0f],
                projectContext,
                excludeTests: true,
                topK: 10);

            results.Should().Contain(match => match.Node.Id == endpoint.Id);
            results.Should().Contain(match => match.Node.Id == service.Id);
            results.Should().Contain(match => match.Node.Id == repository.Id);
            results.Should().NotContain(match => match.Node.Id == testNode.Id);
            results.Should().NotContain(match => match.Node.Id == unrelated.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
