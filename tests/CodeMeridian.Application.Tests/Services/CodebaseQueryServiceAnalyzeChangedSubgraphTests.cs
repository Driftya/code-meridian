using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceAnalyzeChangedSubgraphTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task AnalyzeChangedSubgraphAsync_WithMixedRuntimeChanges_SummarizesRiskTestsArchitectureAndDocs()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var sut = new CodebaseQueryService(graph, vector, Options.Create(WithDotNetTestCommands()));
        var backendNode = Node(
            "method:invite",
            "InviteService.AcceptAsync",
            CodeNodeType.Method,
            "src/Application/Invites/InviteService.cs",
            line: 42,
            project: "Shop",
            fileRole: IndexedFileRole.Source,
            updatedAt: DateTimeOffset.UtcNow);
        var frontendNode = Node(
            "class:panel",
            "InvitePanel",
            CodeNodeType.Class,
            "src/Web/components/invite-panel.tsx",
            line: 8,
            project: "Shop",
            fileRole: IndexedFileRole.Source,
            updatedAt: DateTimeOffset.UtcNow);
        var impactedNode = Node(
            "api:invite",
            "POST /api/invites/{code}/accept",
            CodeNodeType.ApiEndpoint,
            "src/Api/InviteEndpoints.cs",
            project: "Shop",
            fileRole: IndexedFileRole.Source);
        var testNode = Node(
            "test:invite",
            "InviteServiceTests.AcceptAsync_returns_invite_details",
            CodeNodeType.Method,
            "tests/Shop.Application.Tests/Invites/InviteServiceTests.cs",
            line: 17,
            project: "Shop",
            fileRole: IndexedFileRole.Test);
        var architectureTarget = Node(
            "infra:repo",
            "InviteRepository",
            CodeNodeType.Class,
            "src/Infrastructure/Invites/InviteRepository.cs",
            line: 12,
            project: "Shop",
            fileRole: IndexedFileRole.Source);

        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.FilePathFilter == "src/Application/Invites/InviteService.cs" && query.ProjectContext == "Shop"),
                Arg.Any<CancellationToken>())
            .Returns([backendNode]);
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.FilePathFilter == "src/Web/components/invite-panel.tsx" && query.ProjectContext == "Shop"),
                Arg.Any<CancellationToken>())
            .Returns([frontendNode]);
        graph.FindImpactAsync(backendNode.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(impactedNode, 1)]);
        graph.FindImpactAsync(frontendNode.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindRelatedTestsAsync(backendNode.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(testNode, "direct")]);
        graph.FindRelatedTestsAsync(frontendNode.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindHotspotsAsync("Shop", 40, Arg.Any<CancellationToken>())
            .Returns([(backendNode, 9)]);
        graph.FindHighChurnAsync("Shop", 3, Arg.Any<CancellationToken>())
            .Returns([(backendNode, 4), (frontendNode, 5)]);
        graph.FindArchitectureViolationsAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([(backendNode, architectureTarget, "Application -> Infrastructure")]);
        graph.FindSmellPathsAsync("Shop", 4, Arg.Any<CancellationToken>())
            .Returns([
                new DependencySmellPath(
                    "Application -> Infrastructure",
                    backendNode,
                    architectureTarget,
                    1,
                    [
                        new GraphPathStep(backendNode, "Calls", 1.0),
                        new GraphPathStep(architectureTarget, null, null)
                    ])
            ]);
        vector.SearchByTextAsync(Arg.Any<string>(), "Shop", 8, Arg.Any<CancellationToken>())
            .Returns([
                new KnowledgeDocument
                {
                    Id = "doc:invite-feature",
                    Source = "docs/features/invites.md",
                    ProjectContext = "Shop",
                    Content = "Invite acceptance flow."
                }
            ]);

        var result = await sut.AnalyzeChangedSubgraphAsync(
            ["src/Application/Invites/InviteService.cs", "src/Web/components/invite-panel.tsx"],
            projectContext: "Shop",
            impactDepth: 2,
            limit: 8);

        result.Should().Contain("## Changed Subgraph Analysis - Shop");
        result.Should().Contain("**Changed runtimes:** C#, TypeScript/JS");
        result.Should().Contain("**Overall risk:** high");
        result.Should().Contain("InviteService.AcceptAsync");
        result.Should().Contain("InvitePanel");
        result.Should().Contain("no related tests found");
        result.Should().Contain("Architecture violations touching the changed slice");
        result.Should().Contain("Dependency smell paths touching the changed slice");
        result.Should().Contain("docs/features/invites.md");
        result.Should().Contain("dotnet test --filter FullyQualifiedName~");
    }

    [Fact]
    public async Task AnalyzeChangedSubgraphAsync_WithDocsAndTestsOnly_SuppressesStructuralNoise()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var sut = new CodebaseQueryService(graph, vector);
        var docFile = Node(
            "file:doc",
            "55-add-changed-subgraph-analysis.md",
            CodeNodeType.File,
            "docs/features/55-add-changed-subgraph-analysis.md",
            line: 1,
            project: "CodeMeridian",
            fileRole: IndexedFileRole.Unknown);
        var testMethod = Node(
            "test:changed-subgraph",
            "AnalyzeChangedSubgraphAsync_WithMixedRuntimeChanges_SummarizesRiskTestsArchitectureAndDocs",
            CodeNodeType.Method,
            "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs",
            line: 1400,
            project: "CodeMeridian",
            fileRole: IndexedFileRole.Test);

        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.FilePathFilter == "docs/features/55-add-changed-subgraph-analysis.md"),
                Arg.Any<CancellationToken>())
            .Returns([docFile]);
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.FilePathFilter == "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs"),
                Arg.Any<CancellationToken>())
            .Returns([testMethod]);
        vector.SearchByTextAsync(Arg.Any<string>(), "CodeMeridian", 4, Arg.Any<CancellationToken>())
            .Returns([
                new KnowledgeDocument
                {
                    Id = "doc:features-index",
                    Source = "docs/features.md",
                    ProjectContext = "CodeMeridian",
                    Content = "Feature reference."
                }
            ]);

        var result = await sut.AnalyzeChangedSubgraphAsync(
            [
                "docs/features/55-add-changed-subgraph-analysis.md",
                "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs"
            ],
            projectContext: "CodeMeridian",
            limit: 4);

        result.Should().Contain("**Overall risk:** low");
        result.Should().Contain("Production-relevant changed nodes: 0");
        result.Should().Contain("Only docs/test/configuration-style nodes were matched");
        result.Should().Contain("docs/features.md");
        await graph.DidNotReceive().FindArchitectureViolationsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await graph.DidNotReceive().FindSmellPathsAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }


}

