using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindStaleKnowledgeTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindStaleKnowledgeAsync_WhenNoSignals_ReturnsGuidance()
    {
        var (sut, graph) = Build();
        var vector = Substitute.For<IVectorRepository>();
        sut = new CodebaseQueryService(graph, vector);

        vector
            .ListAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new KnowledgeDocument
                {
                    Id = "doc-1",
                    Content = "This document contains general overview text.",
                    Source = "docs/overview.md",
                    ProjectContext = "Shop",
                    UpdatedAt = DateTimeOffset.UtcNow
                }
            ]);

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindUnreferencedAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .GetMostRecentCodeUpdateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow);

        var result = await sut.FindStaleKnowledgeAsync("Shop");

        result.Should().Contain("No obvious stale knowledge found");
        result.Should().Contain("appear consistent");
    }

    [Fact]
    public async Task FindStaleKnowledgeAsync_WithMissingMentionAndOrphanedConcept_ReturnsFindings()
    {
        var (sut, graph) = Build();
        var vector = Substitute.For<IVectorRepository>();
        sut = new CodebaseQueryService(graph, vector);

        vector
            .ListAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new KnowledgeDocument
                {
                    Id = "doc-adr-4",
                    Content = "ADR-004 references PaymentGateway.ChargeAsync",
                    Source = "ADR-004.md",
                    ProjectContext = "Shop",
                    UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30),
                    Metadata = new Dictionary<string, string>
                    {
                        ["mentions"] = "Method:Shop.Payments.PaymentGateway.ChargeAsync"
                    }
                }
            ]);

        graph
            .GetContextForEditingAsync("Method:Shop.Payments.PaymentGateway.ChargeAsync", Arg.Any<CancellationToken>())
            .Returns(new EditingContext(null, [], [], []));
        graph
            .QueryNodesAsync(Arg.Is<CodeGraphQuery>(q => q.TypeFilter == CodeNodeType.ExternalConcept), Arg.Any<CancellationToken>())
            .Returns([new CodeNode { Id = "db:orders", Name = "orders table", Type = CodeNodeType.ExternalConcept, ProjectContext = "Shop" }]);
        graph
            .QueryEdgesAsync("db:orders", 1, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindUnreferencedAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .GetMostRecentCodeUpdateAsync("Shop", Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow);

        var result = await sut.FindStaleKnowledgeAsync("Shop");

        result.Should().Contain("## Stale Knowledge");
        result.Should().Contain("Unresolved documentation references");
        result.Should().Contain("ADR-004");
        result.Should().Contain("PaymentGateway.ChargeAsync");
        result.Should().Contain("Orphaned external concepts");
        result.Should().Contain("orders table");
        result.Should().NotContain("\u00E2");
    }

    [Fact]
    public async Task FindStaleKnowledgeAsync_WithGenericTechAndConfigMentions_DoesNotReportFalsePositives()
    {
        var (sut, graph) = Build();
        var vector = Substitute.For<IVectorRepository>();
        sut = new CodebaseQueryService(graph, vector);

        vector
            .ListAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new KnowledgeDocument
                {
                    Id = "doc-indexer",
                    Content = "Configure TypeScript with meridian.json, mcp.json, config.toml. Example calls: axios.post, client.get, app.MapPost, api.example.com.",
                    Source = "tools/Indexer/README.md",
                    ProjectContext = "Shop",
                    UpdatedAt = DateTimeOffset.UtcNow
                }
            ]);

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindUnreferencedAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .GetMostRecentCodeUpdateAsync("Shop", Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow);

        var result = await sut.FindStaleKnowledgeAsync("Shop");

        result.Should().Contain("No obvious stale knowledge found");
        result.Should().NotContain("axios.post");
        result.Should().NotContain("meridian.json");
        result.Should().NotContain("TypeScript");
    }

    [Fact]
    public async Task FindStaleKnowledgeAsync_WithConfiguredSkipPrefix_SkipsHeuristicMentionScanning()
    {
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            StaleKnowledge = new StaleKnowledgeOptions
            {
                SkipHeuristicSourcePrefixes = ["docs/custom/"]
            }
        });
        var vector = Substitute.For<IVectorRepository>();
        sut = new CodebaseQueryService(graph, vector, Options.Create(new CodebaseAnalysisOptions
        {
            StaleKnowledge = new StaleKnowledgeOptions
            {
                SkipHeuristicSourcePrefixes = ["docs/custom/"]
            }
        }));

        vector
            .ListAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new KnowledgeDocument
                {
                    Id = "doc-custom",
                    Content = "MissingThingService should not be scanned because this source prefix is configured as planning/example docs.",
                    Source = "docs/custom/example.md",
                    ProjectContext = "Shop",
                    UpdatedAt = DateTimeOffset.UtcNow
                }
            ]);

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindUnreferencedAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .GetMostRecentCodeUpdateAsync("Shop", Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow);

        var result = await sut.FindStaleKnowledgeAsync("Shop");

        result.Should().Contain("No obvious stale knowledge found");
        result.Should().NotContain("MissingThingService");
    }


}

