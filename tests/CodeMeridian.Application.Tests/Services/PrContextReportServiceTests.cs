using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class PrContextReportServiceTests
{
    [Fact]
    public async Task BuildAsync_WithChangedCode_SummarizesImpactTestsWarningsAndDocs()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var extraction = new DefaultKeywordExtractionService(Options.Create(new KeywordEnrichmentOptions()));
        var changedNode = new CodeNode
        {
            Id = "CodeMeridian::Method::Shop.Subscriptions.SubscriptionService.SyncAsync()",
            Name = "SubscriptionService.SyncAsync",
            Type = CodeNodeType.Method,
            FilePath = "src/Subscriptions/SubscriptionService.cs",
            ProjectContext = "Shop",
            FileRole = IndexedFileRole.Source,
            Summary = "Synchronize supporter subscription and badge data."
        };
        var impactedNode = new CodeNode
        {
            Id = "CodeMeridian::Class::Shop.Api.SubscriptionController",
            Name = "SubscriptionController",
            Type = CodeNodeType.Class,
            FilePath = "src/Api/SubscriptionController.cs",
            ProjectContext = "Shop",
            FileRole = IndexedFileRole.Source
        };

        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.FilePathFilter == "src/Subscriptions/SubscriptionService.cs" && query.ProjectContext == "Shop"),
                Arg.Any<CancellationToken>())
            .Returns([changedNode]);
        graph.FindImpactAsync(changedNode.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(impactedNode, 1)]);
        graph.FindRelatedTestsAsync(changedNode.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindHotspotsAsync("Shop", 40, Arg.Any<CancellationToken>())
            .Returns([(changedNode, 9)]);
        graph.FindHighChurnAsync("Shop", 3, Arg.Any<CancellationToken>())
            .Returns([(changedNode, 4)]);
        vector.ListAsync("Shop", 250, Arg.Any<CancellationToken>())
            .Returns([
                new KnowledgeDocument
                {
                    Id = "doc:subscription-feature",
                    Source = "docs/features/subscriptions.md",
                    ProjectContext = "Shop",
                    Content = "Subscription feature review covers supporter badge synchronization and webhook flows."
                },
                new KnowledgeDocument
                {
                    Id = "doc:security",
                    Source = "docs/security.md",
                    ProjectContext = "Shop",
                    Content = "Security hardening guide."
                }
            ]);

        var sut = new PrContextReportService(graph, vector, extraction);

        var report = await sut.BuildAsync(new PrContextReportRequest(
            "Shop",
            ["src/Subscriptions/SubscriptionService.cs"],
            BaseRef: "origin/main",
            HeadRef: "HEAD",
            IncludeDocs: true));

        report.ChangedFiles.Should().ContainSingle("src/Subscriptions/SubscriptionService.cs");
        report.ChangedNodes.Should().ContainSingle(node => node.Id == changedNode.Id);
        report.ImpactedNodes.Should().ContainSingle(item => item.Node.Id == impactedNode.Id && item.Distance == 1);
        report.MissingTestNodes.Should().ContainSingle(node => node.Id == changedNode.Id);
        report.HotspotWarnings.Should().ContainSingle();
        report.HotspotWarnings[0].Reason.Should().Contain("High fan-in hotspot");
        report.HotspotWarnings[0].Reason.Should().Contain("Frequently re-indexed");
        report.RelatedDocuments.Should().ContainSingle(doc => doc.Source == "docs/features/subscriptions.md" && doc.Confidence == "High");
        report.RelatedDocuments[0].MatchedKeywords.Should().Contain(keyword => keyword.Contains("subscription", StringComparison.Ordinal));
        report.ReviewFocus.Should().Contain(item => item.Contains("regression coverage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildAsync_WithNoChangedFiles_ReturnsEmptyReport()
    {
        var sut = new PrContextReportService(
            Substitute.For<ICodeGraphRepository>(),
            Substitute.For<IVectorRepository>(),
            new DefaultKeywordExtractionService(Options.Create(new KeywordEnrichmentOptions())));

        var report = await sut.BuildAsync(new PrContextReportRequest("Shop", []));

        report.ChangedFiles.Should().BeEmpty();
        report.ChangedNodes.Should().BeEmpty();
        report.ImpactedNodes.Should().BeEmpty();
        report.RelatedDocuments.Should().BeEmpty();
        report.ReviewFocus.Should().ContainSingle();
    }

    [Fact]
    public async Task BuildAsync_WithDocsAndTestsOnly_SuppressesStructuralNoiseAndKeepsRepresentativeNodes()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var extraction = new DefaultKeywordExtractionService(Options.Create(new KeywordEnrichmentOptions()));
        var changedFiles = new[]
        {
            "docs/features/30-add-blast-radius-with-confidence.md",
            "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs"
        };
        var testNodes = new[]
        {
            new CodeNode
            {
                Id = "file:test",
                Name = "CodebaseQueryServiceAnalyticsTests.cs",
                Type = CodeNodeType.File,
                FilePath = changedFiles[1],
                ProjectContext = "CodeMeridian",
                FileRole = IndexedFileRole.Test,
                LineNumber = 1
            },
            new CodeNode
            {
                Id = "class:test",
                Name = "CodebaseQueryServiceAnalyticsTests",
                Type = CodeNodeType.Class,
                FilePath = changedFiles[1],
                ProjectContext = "CodeMeridian",
                FileRole = IndexedFileRole.Test,
                LineNumber = 16
            },
            new CodeNode
            {
                Id = "method:build",
                Name = "Build()",
                Type = CodeNodeType.Method,
                FilePath = changedFiles[1],
                ProjectContext = "CodeMeridian",
                FileRole = IndexedFileRole.Test,
                LineNumber = 20
            },
            new CodeNode
            {
                Id = "method:old",
                Name = "FindImpactAsync_WhenNoCallers_ReturnsGuidanceMessage()",
                Type = CodeNodeType.Method,
                FilePath = changedFiles[1],
                ProjectContext = "CodeMeridian",
                FileRole = IndexedFileRole.Test,
                LineNumber = 121
            },
            new CodeNode
            {
                Id = "method:new-1",
                Name = "FindImpactAsync_WithConfidence_SeparatesProvenHeuristicAndUnknownRisk()",
                Type = CodeNodeType.Method,
                FilePath = changedFiles[1],
                ProjectContext = "CodeMeridian",
                FileRole = IndexedFileRole.Test,
                LineNumber = 155
            },
            new CodeNode
            {
                Id = "method:new-2",
                Name = "FindImpactAsync_WithConfidenceSummary_ReturnsConfidenceCounts()",
                Type = CodeNodeType.Method,
                FilePath = changedFiles[1],
                ProjectContext = "CodeMeridian",
                FileRole = IndexedFileRole.Test,
                LineNumber = 207
            },
            new CodeNode
            {
                Id = "method:new-3",
                Name = "FindImpactAsync_WithConfidence_WhenNoCallers_ReturnsGuidanceMessage()",
                Type = CodeNodeType.Method,
                FilePath = changedFiles[1],
                ProjectContext = "CodeMeridian",
                FileRole = IndexedFileRole.Test,
                LineNumber = 244
            }
        };
        var impactedNode = new CodeNode
        {
            Id = "prod:impact",
            Name = "CodebaseStatusService",
            Type = CodeNodeType.Class,
            FilePath = "src/Application/Services/CodebaseStatusService.cs",
            ProjectContext = "CodeMeridian",
            FileRole = IndexedFileRole.Source
        };

        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.FilePathFilter == changedFiles[1]),
                Arg.Any<CancellationToken>())
            .Returns(testNodes);
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.FilePathFilter == changedFiles[0]),
                Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindImpactAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([(impactedNode, 1)]);
        graph.FindHotspotsAsync("CodeMeridian", 40, Arg.Any<CancellationToken>())
            .Returns([(testNodes[2], 99)]);
        graph.FindHighChurnAsync("CodeMeridian", 3, Arg.Any<CancellationToken>())
            .Returns([(testNodes[2], 4)]);

        var sut = new PrContextReportService(graph, vector, extraction);

        var report = await sut.BuildAsync(new PrContextReportRequest(
            "CodeMeridian",
            changedFiles,
            IncludeDocs: false));

        report.ChangedNodes.Select(node => node.Id).Should().BeEquivalentTo([
            "file:test",
            "class:test",
            "method:new-2",
            "method:new-3"
        ]);
        report.ImpactedNodes.Should().BeEmpty();
        report.HotspotWarnings.Should().BeEmpty();
        report.ReviewFocus.Should().Contain(item => item.Contains("docs-only or test-only", StringComparison.OrdinalIgnoreCase));
        await graph.DidNotReceive().FindImpactAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuildAsync_DeduplicatesRelatedDocumentsBySource()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var extraction = new DefaultKeywordExtractionService(Options.Create(new KeywordEnrichmentOptions()));
        var changedNode = new CodeNode
        {
            Id = "CodeMeridian::Method::Shop.Subscriptions.SubscriptionService.SyncAsync()",
            Name = "SubscriptionService.SyncAsync",
            Type = CodeNodeType.Method,
            FilePath = "src/Subscriptions/SubscriptionService.cs",
            ProjectContext = "Shop",
            FileRole = IndexedFileRole.Source,
            Summary = "Synchronize supporter subscription and badge data."
        };

        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.FilePathFilter == "src/Subscriptions/SubscriptionService.cs" && query.ProjectContext == "Shop"),
                Arg.Any<CancellationToken>())
            .Returns([changedNode]);
        graph.FindImpactAsync(changedNode.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindRelatedTestsAsync(changedNode.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(changedNode, "Direct")]);
        graph.FindHotspotsAsync("Shop", 40, Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindHighChurnAsync("Shop", 3, Arg.Any<CancellationToken>())
            .Returns([]);
        vector.ListAsync("Shop", 250, Arg.Any<CancellationToken>())
            .Returns([
                new KnowledgeDocument
                {
                    Id = "doc:subscription-feature-a",
                    Source = "docs/features/subscriptions.md",
                    ProjectContext = "Shop",
                    Content = "Subscription feature review covers supporter badge synchronization."
                },
                new KnowledgeDocument
                {
                    Id = "doc:subscription-feature-b",
                    Source = "docs/features/subscriptions.md",
                    ProjectContext = "Shop",
                    Content = "Subscription docs mention badge flows and webhook processing."
                }
            ]);

        var sut = new PrContextReportService(graph, vector, extraction);

        var report = await sut.BuildAsync(new PrContextReportRequest(
            "Shop",
            ["src/Subscriptions/SubscriptionService.cs"],
            IncludeDocs: true));

        report.RelatedDocuments.Should().ContainSingle();
        report.RelatedDocuments[0].Source.Should().Be("docs/features/subscriptions.md");
    }

    [Fact]
    public async Task BuildAsync_WhenChangedNodeIsUnshielded_MatchesFindTestShieldMissingTestRisk()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var extraction = new DefaultKeywordExtractionService(Options.Create(new KeywordEnrichmentOptions()));
        var changedNode = new CodeNode
        {
            Id = "CodeMeridian::Method::Shop.Orders.OrderService.PlaceOrderAsync()",
            Name = "OrderService.PlaceOrderAsync",
            Type = CodeNodeType.Method,
            FilePath = "src/Orders/OrderService.cs",
            ProjectContext = "Shop",
            FileRole = IndexedFileRole.Source,
            LineNumber = 42
        };

        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.FilePathFilter == "src/Orders/OrderService.cs" && query.ProjectContext == "Shop"),
                Arg.Any<CancellationToken>())
            .Returns([changedNode]);
        graph.GetContextForEditingAsync(changedNode.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(changedNode, [], [], []));
        graph.FindImpactAsync(changedNode.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindRelatedTestsAsync(changedNode.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindHotspotsAsync("Shop", 40, Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindHighChurnAsync("Shop", 3, Arg.Any<CancellationToken>())
            .Returns([]);

        var reportService = new PrContextReportService(graph, vector, extraction);
        var queryService = new CodebaseQueryService(graph, vector);

        var shield = await queryService.FindTestShieldAsync(changedNode.Id, projectContext: "Shop", depth: 2);
        var report = await reportService.BuildAsync(new PrContextReportRequest(
            "Shop",
            ["src/Orders/OrderService.cs"],
            IncludeDocs: false));

        shield.Should().Contain("**Shield summary:** 0 direct, 0 primary, 0 secondary, 1 unshielded path nodes");
        shield.Should().Contain("### Unshielded path nodes (1)");
        shield.Should().Contain("`OrderService.PlaceOrderAsync`");
        report.MissingTestNodes.Should().ContainSingle(node => node.Id == changedNode.Id);
        report.ReviewFocus.Should().Contain(item =>
            item.Contains("Add or update focused regression coverage", StringComparison.OrdinalIgnoreCase)
            && item.Contains("1", StringComparison.Ordinal));
    }
}
