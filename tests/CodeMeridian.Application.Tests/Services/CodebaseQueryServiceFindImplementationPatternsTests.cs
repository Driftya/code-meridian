using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindImplementationPatternsTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindImplementationPatternsAsync_WhenNoPatterns_ReturnsGuidance()
    {
        var (sut, graph) = Build();
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.FindImplementationPatternsAsync("invite acceptance flow", projectContext: "Shop");

        result.Should().Contain("No implementation patterns found");
        result.Should().Contain("invite acceptance flow");
    }

    [Fact]
    public async Task FindImplementationPatternsAsync_WithStructuralMatches_ReturnsRankedPatterns()
    {
        var (sut, graph, embeddings) = BuildWithEmbeddings();
        var expectedEmbedding = new[] { 0.6f, 0.2f, 0.1f, 0.1f };
        var endpoint = Node(
            "endpoint-1",
            "POST /api/invites/{id}/accept",
            CodeNodeType.ApiEndpoint,
            "src/Api/Invites/AcceptInviteEndpoint.cs",
            12,
            "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            fileRole: IndexedFileRole.Source,
            @namespace: "Shop.Api.Invites");
        var service = Node(
            "service-1",
            "AcceptInviteService",
            CodeNodeType.Class,
            "src/Application/Invites/AcceptInviteService.cs",
            18,
            "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            fileRole: IndexedFileRole.Source,
            @namespace: "Shop.Application.Invites");
        var contract = Node(
            "contract-1",
            "IAcceptInviteRepository",
            CodeNodeType.Interface,
            "src/Application/Invites/IAcceptInviteRepository.cs",
            4,
            "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            fileRole: IndexedFileRole.Source,
            @namespace: "Shop.Application.Invites");
        var repository = Node(
            "repository-1",
            "InviteRepository",
            CodeNodeType.Class,
            "src/Infrastructure/Invites/InviteRepository.cs",
            20,
            "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            fileRole: IndexedFileRole.Source,
            @namespace: "Shop.Infrastructure.Invites");
        var directTest = Node(
            "test-1",
            "AcceptInviteServiceTests",
            CodeNodeType.Class,
            "tests/Application/Invites/AcceptInviteServiceTests.cs",
            9,
            "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            fileRole: IndexedFileRole.Test,
            @namespace: "Shop.Tests.Invites");
        var boundary = new CodeNode
        {
            Id = "boundary-1",
            Name = "InviteAcceptanceWrite",
            Type = CodeNodeType.ExternalConcept,
            FilePath = "src/Infrastructure/Invites/InviteRepository.cs",
            LineNumber = 38,
            ProjectContext = "Shop",
            UpdatedAt = DateTimeOffset.UtcNow,
            FileRole = IndexedFileRole.Source,
            Properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "DatabaseOperation"
            }
        };

        embeddings.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        embeddings.GenerateEmbeddingAsync("invite acceptance flow", Arg.Any<CancellationToken>())
            .Returns(expectedEmbedding);
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([endpoint]);
        graph.FindImplementationPatternCandidatesAsync(
                Arg.Is<float[]>(embedding => embedding.SequenceEqual(expectedEmbedding)),
                "Shop",
                true,
                24,
                Arg.Any<CancellationToken>())
            .Returns([(endpoint, 0.93)]);
        graph.GetContextForEditingAsync(endpoint.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(endpoint, [], [service], [contract]));
        graph.FindImpactAsync(endpoint.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindDownstreamAsync(endpoint.Id, 3, Arg.Any<CancellationToken>())
            .Returns([(repository, 1), (boundary, 2)]);
        graph.FindRelatedTestsAsync(endpoint.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(directTest, "direct")]);

        var result = await sut.FindImplementationPatternsAsync("invite acceptance flow", projectContext: "Shop");

        result.Should().Contain("## Structural Implementation Patterns");
        result.Should().Contain("embedding and lexical graph seeds with structural reranking");
        result.Should().Contain("api/command entry -> application/domain -> contract -> repository/store -> database/event boundary -> tests");
        result.Should().Contain("POST /api/invites/{id}/accept");
        result.Should().Contain("AcceptInviteService");
        result.Should().Contain("InviteRepository");
        result.Should().Contain("AcceptInviteServiceTests");
        result.Should().Contain("### Pattern details");
        await graph.Received(1).FindImplementationPatternCandidatesAsync(Arg.Any<float[]>(), "Shop", true, 24, Arg.Any<CancellationToken>());
    }


}

