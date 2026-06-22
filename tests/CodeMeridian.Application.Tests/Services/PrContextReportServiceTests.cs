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
}
