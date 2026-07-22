using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindImplementationSurfaceTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindImplementationSurfaceAsync_WithMatchingNodes_RanksLikelyFiles()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("m1", "FindStaleKnowledgeAsync", CodeNodeType.Method, "TODO.md", 1, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow, sourceHash: "todo"),
                Node("c1", "CodebaseQueryService", CodeNodeType.Class, "src/Application/Services/CodebaseQueryService.Analytics.cs", 1, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow, sourceHash: "code")
            ]);

        var result = await sut.FindImplementationSurfaceAsync(
            "add stale knowledge query",
            "stale,knowledge",
            "CodeMeridian");

        result.Should().Contain("## Implementation Surface");
        result.Should().Contain("### Primary Edit Targets");
        result.Should().Contain("### Context-Only Targets");
        result.Should().Contain("TODO.md");
        result.Should().Contain("FindStaleKnowledgeAsync");
        result.Should().Contain("Target confidence");
        result.Should().Contain("file-only");
        result.Should().Contain("documentation file is context, not the edit surface");
        result.Should().Contain("Freshness");
        result.IndexOf("src/Application/Services/CodebaseQueryService.Analytics.cs", StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf("TODO.md", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindImplementationSurfaceAsync_WithFrontendConceptMatch_ExpandsToConnectedMarkupAndStylesheetFiles()
    {
        var (sut, graph) = Build();
        var heroClass = new CodeNode
        {
            Id = "Shop.Web:ExternalConcept:CssClass:hero",
            Name = "hero",
            Type = CodeNodeType.ExternalConcept,
            ProjectContext = "Shop.Web",
            Properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "CssClass"
            }
        };
        var markupFile = Node("markup", "HeroCard.tsx", CodeNodeType.File, "src/web/HeroCard.tsx", 1, "Shop.Web");
        var stylesheetFile = Node("style", "HeroCard.scss", CodeNodeType.File, "src/web/HeroCard.scss", 1, "Shop.Web");
        var selectorNode = new CodeNode
        {
            Id = "Shop.Web:ExternalConcept:CssSelector:hero",
            Name = ".hero",
            Type = CodeNodeType.ExternalConcept,
            ProjectContext = "Shop.Web",
            Properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "CssSelector"
            }
        };

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([heroClass]);
        graph
            .FindImpactAsync(heroClass.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(markupFile, 1), (selectorNode, 1)]);
        graph
            .FindDownstreamAsync(heroClass.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(selectorNode, 1), (stylesheetFile, 2)]);

        var result = await sut.FindImplementationSurfaceAsync(
            "update hero class styling",
            "hero,scss",
            "Shop.Web");

        result.Should().Contain("src/web/HeroCard.tsx");
        result.Should().Contain("src/web/HeroCard.scss");
        result.Should().Contain("frontend graph matches");
        result.Should().Contain("Target confidence");
    }

    [Fact]
    public async Task FindImplementationSurfaceAsync_UsesPrecisionFeedbackToExplainAcceptedAndIgnoredTargets()
    {
        var feedbackPath = WritePrecisionFeedbackFile(
            """
            {
              "project": "CodeMeridian",
              "sessionFile": ".meridian/sessions/session.jsonl",
              "generatedAtUtc": "2026-06-19T00:00:00Z",
              "tools": [
                {
                  "toolName": "mcp__CodeMeridian.find_implementation_surface",
                  "suggestedFileCount": 2,
                  "acceptedFileCount": 1,
                  "ignoredFileCount": 1,
                  "suggestedTestCount": 0,
                  "acceptedTestCount": 0,
                  "ignoredTestCount": 0,
                  "exactTargets": 1,
                  "fileOnlyTargets": 1,
                  "heuristicTargets": 0,
                  "staleTargets": 0,
                  "staleWarnings": 0,
                  "manualFallbackCommands": 0,
                  "files": [
                    { "path": "src/Application/Preferred.cs", "suggestedCount": 1, "acceptedCount": 1, "ignoredCount": 0 },
                    { "path": "src/Application/Broad.cs", "suggestedCount": 1, "acceptedCount": 0, "ignoredCount": 1 }
                  ],
                  "tests": []
                }
              ]
            }
            """);
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            PrecisionFeedback = new PrecisionFeedbackOptions
            {
                FeedbackFilePath = feedbackPath
            }
        });
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("preferred", "PreferredTarget", CodeNodeType.Class, "src/Application/Preferred.cs", 1, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow, sourceHash: "abc"),
                Node("broad", "BroadTarget", CodeNodeType.Class, "src/Application/Broad.cs", 1, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow, sourceHash: "def")
            ]);

        var result = await sut.FindImplementationSurfaceAsync("preferred target", projectContext: "CodeMeridian");

        result.IndexOf("src/Application/Preferred.cs", StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf("src/Application/Broad.cs", StringComparison.Ordinal));
        result.Should().Contain("feedback accepted 1/1 prior sessions");
        result.Should().Contain("feedback ignored 1/1 prior sessions");
    }

    [Fact]
    public async Task FindImplementationSurfaceAsync_PrimaryTargetCanDrivePlanEditRouteAnchor()
    {
        var (sut, graph) = Build();
        var service = Node("s1", "PaymentService", CodeNodeType.Class, "src/Application/Payments/PaymentService.cs", 12, "Shop", updatedAt: DateTimeOffset.UtcNow, sourceHash: "svc");
        var implementation = Node("r1", "SqlPaymentRepository", CodeNodeType.Class, "src/Infrastructure/Payments/SqlPaymentRepository.cs", 9, "Shop");
        var endpoint = Node("e1", "PaymentEndpoint", CodeNodeType.Method, "src/McpServer/Api/PaymentEndpoint.cs", 20, "Shop");
        var test = Node("t1", "PaymentServiceTests", CodeNodeType.Class, "tests/Shop.Tests/PaymentServiceTests.cs", 8, "Shop");

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([service]);
        graph
            .GetContextForEditingAsync(service.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(service, [endpoint], [implementation], []));
        graph
            .FindImpactAsync(service.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(endpoint, 1)]);
        graph
            .FindDownstreamAsync(service.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(implementation, 1)]);
        graph
            .FindRelatedTestsAsync(service.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(test, "direct")]);

        var surface = await sut.FindImplementationSurfaceAsync(
            "replace repository pattern in payments",
            "repository,payments",
            "Shop");
        var route = await sut.PlanEditRouteAsync(
            "replace repository pattern in payments",
            "repository,payments",
            "Shop");

        surface.Should().Contain("`src/Application/Payments/PaymentService.cs`");
        route.Should().Contain("**Anchor:** `PaymentService` (Class) - `src/Application/Payments/PaymentService.cs`");
        route.Should().Contain("Route confidence:** High");
        route.Should().Contain("Run `build_minimal_context` on exact route targets before changing code.");
    }

    [Fact]
    public async Task FindImplementationSurfaceAsync_PrunesTestsGeneratedAndBroadFileOnlyTargetsIntoContextOnly()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("impl", "ChargeAsync", CodeNodeType.Method, "src/Payments/PaymentGateway.cs", 42, "Shop", updatedAt: DateTimeOffset.UtcNow, sourceHash: "abc", fileRole: IndexedFileRole.Source),
                Node("test", "PaymentGatewayTests", CodeNodeType.Class, "tests/Payments/PaymentGatewayTests.cs", 5, "Shop", updatedAt: DateTimeOffset.UtcNow, sourceHash: "def", fileRole: IndexedFileRole.Test),
                Node("gen", "PaymentGateway.Generated", CodeNodeType.Class, "src/Generated/PaymentGateway.g.cs", 1, "Shop", updatedAt: DateTimeOffset.UtcNow, sourceHash: "ghi", fileRole: IndexedFileRole.Generated),
                Node("fileOnly", "PaymentGateway.cs", CodeNodeType.File, "src/Payments/PaymentGateway.csproj.user", 1, "Shop", updatedAt: DateTimeOffset.UtcNow, sourceHash: "jkl")
            ]);

        var result = await sut.FindImplementationSurfaceAsync(
            "update payment gateway charge flow",
            "payment,gateway,charge",
            "Shop");

        result.Should().Contain("### Primary Edit Targets");
        result.Should().Contain("`src/Payments/PaymentGateway.cs`");
        result.Should().Contain("### Context-Only Targets");
        result.Should().Contain("tests/Payments/PaymentGatewayTests.cs");
        result.Should().Contain("test target is verification context");
        result.Should().Contain("src/Generated/PaymentGateway.g.cs");
        result.Should().Contain("generated file should not be the primary edit surface");
        result.Should().Contain("src/Payments/PaymentGateway.csproj.user");
        result.Should().Contain("broad file match without an edit-ready symbol anchor");
    }

    [Fact]
    public async Task FindImplementationSurfaceAsync_WhenOnlyContextCandidatesExist_PromotesBestAvailableTarget()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("doc", "Architecture Note", CodeNodeType.File, "docs/architecture.md", 1, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow),
                Node("todo", "TODO", CodeNodeType.File, "TODO.md", 1, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow)
            ]);

        var result = await sut.FindImplementationSurfaceAsync(
            "architecture note update",
            projectContext: "CodeMeridian");

        result.Should().Contain("### Primary Edit Targets");
        result.Should().Contain("docs/architecture.md");
        result.Should().Contain("### Context-Only Targets");
        result.Should().Contain("TODO.md");
    }

    [Fact]
    public async Task FindImplementationSurfaceAsync_PrefersGoalTermMatchesOverBroadRepositoryInterfaces()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("repo", "ICodeGraphRepository", CodeNodeType.Interface, "src/Core/CodeGraph/ICodeGraphRepository.cs", 1, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow, sourceHash: "repo"),
                Node("stale", "FindStaleKnowledgeAsync", CodeNodeType.Method, "src/Application/Services/CodebaseQueryService.Analytics.cs", 42, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow, sourceHash: "stale", summary: "Detects stale knowledge after renames and reindexing.")
            ]);

        var result = await sut.FindImplementationSurfaceAsync(
            "add stale knowledge query",
            projectContext: "CodeMeridian");

        result.IndexOf("src/Application/Services/CodebaseQueryService.Analytics.cs", StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf("src/Core/CodeGraph/ICodeGraphRepository.cs", StringComparison.Ordinal));
        result.Should().Contain("goal-term matches");
    }

    [Fact]
    public async Task FindImplementationSurfaceAsync_IncrementalCallEdgeGoal_PromotesConcreteIndexerPipeline()
    {
        var (sut, graph) = Build();
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>()).Returns([
            Node("resolver", "CSharpCallEdgeResolver", CodeNodeType.Class, "tools/GenericIndexer/Pipeline/CSharpCallEdgeResolver.cs", project: "Project", updatedAt: DateTimeOffset.UtcNow, sourceHash: "a"),
            Node("indexer", "CSharpIndexer", CodeNodeType.Class, "tools/GenericIndexer/Pipeline/CSharpIndexer.cs", project: "Project", updatedAt: DateTimeOffset.UtcNow, sourceHash: "b"),
            Node("pipeline", "IndexerPipeline", CodeNodeType.Class, "tools/GenericIndexer/Pipeline/IndexerPipeline.cs", project: "Project", updatedAt: DateTimeOffset.UtcNow, sourceHash: "c"),
            Node("contract", "ICodeGraphRepository", CodeNodeType.Interface, "src/Core/ICodeGraphRepository.cs", project: "Project", updatedAt: DateTimeOffset.UtcNow, sourceHash: "d")
        ]);

        var result = await sut.FindImplementationSurfaceAsync(
            "preserve incremental cross-file call edge resolution",
            "incremental indexing,call edge,resolution",
            "Project");

        var primary = result[..result.IndexOf("### Context-Only Targets", StringComparison.Ordinal)];
        primary.Should().Contain("CSharpCallEdgeResolver.cs");
        primary.Should().Contain("CSharpIndexer.cs");
        primary.Should().Contain("IndexerPipeline.cs");
        primary.Should().NotContain("ICodeGraphRepository.cs");
    }

    [Fact]
    public async Task FindImplementationSurfaceAsync_TestRoleGoal_PromotesRoleOptionsAndClassifier()
    {
        var (sut, graph) = Build();
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>()).Returns([
            Node("options", "IndexedFileRoleOptions", CodeNodeType.Class, "src/Application/IndexedFileRoleOptions.cs", project: "Project", updatedAt: DateTimeOffset.UtcNow, sourceHash: "a"),
            Node("classifier", "ConfiguredIndexedFileRoleClassifier", CodeNodeType.Class, "src/Application/ConfiguredIndexedFileRoleClassifier.cs", project: "Project", updatedAt: DateTimeOffset.UtcNow, sourceHash: "b"),
            Node("runner", "NodeProcessRunner", CodeNodeType.Class, "tools/Indexer/NodeProcessRunner.cs", project: "Project", updatedAt: DateTimeOffset.UtcNow, sourceHash: "c")
        ]);

        var result = await sut.FindImplementationSurfaceAsync(
            "classify helper files under test directories as tests",
            "file role,test classification",
            "Project");

        var primary = result[..result.IndexOf("### Context-Only Targets", StringComparison.Ordinal)];
        primary.Should().Contain("IndexedFileRoleOptions.cs");
        primary.Should().Contain("ConfiguredIndexedFileRoleClassifier.cs");
        primary.Should().NotContain("NodeProcessRunner.cs");
    }

    [Fact]
    public async Task FindImplementationSurfaceAsync_FileOnlyTarget_PairsWithResolveExactSymbolGuidance()
    {
        var (sut, graph) = Build();
        var fileOnly = Node(
            "File:CodeMeridian.Application.Services.CodebaseQueryService.Surface.cs",
            "CodebaseQueryService.Surface.cs",
            CodeNodeType.File,
            "src/Application/Services/CodebaseQueryService.Surface.cs",
            1,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            sourceHash: "surface-file");
        var exact = Node(
            "Method:CodeMeridian.Application.Services.CodebaseQueryService.FindImplementationSurfaceAsync",
            "FindImplementationSurfaceAsync",
            CodeNodeType.Method,
            "src/Application/Services/CodebaseQueryService.Surface.cs",
            8,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 40);

        graph
            .QueryNodesAsync(
                Arg.Any<CodeGraphQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var query = ci.Arg<CodeGraphQuery>();
                if (query.NameFilter == "FindImplementationSurfaceAsync")
                    return [exact];

                return [fileOnly];
            });

        var surface = await sut.FindImplementationSurfaceAsync(
            "find implementation surface ranking",
            projectContext: "CodeMeridian");
        var resolved = await sut.ResolveExactSymbolAsync(
            "FindImplementationSurfaceAsync",
            "src/Application/Services/CodebaseQueryService.Surface.cs",
            line: 10,
            projectContext: "CodeMeridian");

        surface.Should().Contain("Target confidence");
        surface.Should().Contain("file-only");
        surface.Should().Contain("Use `resolve_exact_symbol` when target confidence is not exact.");
        resolved.Should().Contain("**Confidence summary:** 1 exact");
        resolved.Should().Contain("FindImplementationSurfaceAsync");
    }


}

