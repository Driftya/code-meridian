using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

public sealed class Neo4jCodeGraphRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly Neo4jOptions _options;
    private Neo4jCodeGraphRepository? _repository;

    public Neo4jCodeGraphRepositoryIntegrationTests()
    {
        _options = TestEnvironment.TryGetNeo4jOptions()
            ?? throw new InvalidOperationException("Neo4j connection details were not found in environment or repo .env.");
    }

    public async Task InitializeAsync()
    {
        _repository = new Neo4jCodeGraphRepository(Options.Create(_options), NullLogger<Neo4jCodeGraphRepository>.Instance);
        await _repository.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_repository is not null)
            await _repository.DisposeAsync();
    }

    [Fact]
    public async Task QueryNodesAsync_ForCodeMeridian_ReturnsKnownSurfaces()
    {
        var results = await _repository!.QueryNodesAsync(
            new CodeGraphQuery
            {
                ProjectContext = null,
                Limit = 25
            });

        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task QueryNodesAsync_WithFilePathFilter_ReturnsNodesFromMatchingFile()
    {
        var target = await FindAnyTargetAsync();
        target.Should().NotBeNull("the CodeMeridian graph should already contain indexed nodes");
        target!.FilePath.Should().NotBeNullOrWhiteSpace("exact symbol resolution depends on indexed file paths");

        var results = await _repository!.QueryNodesAsync(
            new CodeGraphQuery
            {
                ProjectContext = target.ProjectContext,
                FilePathFilter = target.FilePath,
                Limit = 25
            });

        results.Should().NotBeEmpty();
        results.Should().OnlyContain(node =>
            string.Equals(node.FilePath, target.FilePath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CountMethods_ForExistingGraph_ReturnReasonableValues()
    {
        var projectContext = $"Integration.Counts.{Guid.NewGuid():N}";
        var filePath = $"src/{projectContext}/Target.cs";
        var caller = CreateNode(
            id: $"{projectContext}.Caller",
            name: "Caller",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: filePath,
            namespaceName: $"{projectContext}.Target");
        var callee = CreateNode(
            id: $"{projectContext}.Callee",
            name: "Callee",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: filePath,
            namespaceName: $"{projectContext}.Target");
        var diagnostic = CreateNode(
            id: $"{projectContext}.Diag",
            name: "Error CS9999",
            type: CodeNodeType.Diagnostic,
            projectContext: projectContext,
            filePath: filePath,
            namespaceName: $"{projectContext}.Target",
            lineNumber: 1,
            summary: "Synthetic diagnostic for count coverage");

        try
        {
            await _repository!.UpsertNodeAsync(caller);
            await _repository.UpsertNodeAsync(callee);
            await _repository.UpsertNodeAsync(diagnostic);
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = caller.Id,
                TargetId = callee.Id,
                Type = CodeEdgeType.Calls
            });

            var nodeCount = await _repository.CountCodeNodesAsync(projectContext);
            var callCount = await _repository.CountCallEdgesAsync(projectContext);
            var diagnosticCount = await _repository.CountDiagnosticsAsync(projectContext);

            nodeCount.Should().BeGreaterOrEqualTo(2);
            callCount.Should().BeGreaterOrEqualTo(1);
            diagnosticCount.Should().BeGreaterOrEqualTo(1);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task ProjectScopedQueries_AreCaseInsensitive()
    {
        var projectContext = $"Integration.Case.{Guid.NewGuid():N}";
        var node = CreateNode(
            id: $"{projectContext}.Target",
            name: "Target",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Target.cs",
            namespaceName: $"{projectContext}.Target");

        try
        {
            await _repository!.UpsertNodeAsync(node);

            var lowerCaseProject = projectContext.ToLowerInvariant();
            var matches = await _repository.QueryNodesAsync(new CodeGraphQuery
            {
                ProjectContext = lowerCaseProject,
                NameFilter = "Target",
                Limit = 10
            });
            var count = await _repository.CountCodeNodesAsync(lowerCaseProject);

            matches.Should().Contain(match => match.Id == node.Id);
            count.Should().BeGreaterOrEqualTo(1);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindHybridMatchesAsync_WithAnchorAndEmbedding_ReturnsNearbySemanticMatches()
    {
        var projectContext = $"Integration.Hybrid.{Guid.NewGuid():N}";
        var anchor = CreateNode(
            id: $"{projectContext}.OrderService",
            name: "OrderService",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrderService.cs",
            namespaceName: $"{projectContext}.Application",
            embedding: [0.1f, 0.2f, 0.3f, 0.4f]);
        var nearby = CreateNode(
            id: $"{projectContext}.RetryPolicy",
            name: "RetryPolicy",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/RetryPolicy.cs",
            namespaceName: $"{projectContext}.Application",
            embedding: [0.11f, 0.19f, 0.29f, 0.39f]);
        var distant = CreateNode(
            id: $"{projectContext}.OutsideScope",
            name: "OutsideScope",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OutsideScope.cs",
            namespaceName: $"{projectContext}.Application",
            embedding: [0.11f, 0.19f, 0.29f, 0.39f]);

        try
        {
            await _repository!.UpsertNodeAsync(anchor);
            await _repository.UpsertNodeAsync(nearby);
            await _repository.UpsertNodeAsync(distant);
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = anchor.Id,
                TargetId = nearby.Id,
                Type = CodeEdgeType.Calls
            });

            var results = await _repository.FindHybridMatchesAsync(
                [0.1f, 0.2f, 0.3f, 0.4f],
                nearNodeId: anchor.Id,
                maxHops: 2,
                projectContext: projectContext,
                excludeTests: true,
                topK: 10);

            results.Should().ContainSingle(match => match.Node.Id == nearby.Id);
            results.Should().NotContain(match => match.Node.Id == distant.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

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

    [Fact]
    public async Task RiskCoreGdsQueries_UseSharedStructuralRelationships()
    {
        var projectContext = $"Integration.RiskyCore.{Guid.NewGuid():N}";
        var entry = CreateNode(
            id: $"{projectContext}.OrdersPage",
            name: "OrdersPage.tsx",
            type: CodeNodeType.File,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrdersPage.tsx",
            namespaceName: $"{projectContext}.Frontend");
        var endpoint = CreateNode(
            id: $"{projectContext}.PostOrders",
            name: "POST /api/orders",
            type: CodeNodeType.ApiEndpoint,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrdersEndpoint.cs",
            namespaceName: $"{projectContext}.Api");
        var service = CreateNode(
            id: $"{projectContext}.CreateOrderService",
            name: "CreateOrderService",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CreateOrderService.cs",
            namespaceName: $"{projectContext}.Application");
        var store = CreateNode(
            id: $"{projectContext}.OrdersTable",
            name: "Orders",
            type: CodeNodeType.DatabaseTable,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Orders.sql",
            namespaceName: $"{projectContext}.Infrastructure");

        try
        {
            await _repository!.UpsertNodeAsync(entry);
            await _repository.UpsertNodeAsync(endpoint);
            await _repository.UpsertNodeAsync(service);
            await _repository.UpsertNodeAsync(store);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = entry.Id,
                TargetId = endpoint.Id,
                Type = CodeEdgeType.Uses
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = endpoint.Id,
                TargetId = service.Id,
                Type = CodeEdgeType.DependsOn
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = service.Id,
                TargetId = store.Id,
                Type = CodeEdgeType.Writes
            });

            var pageRank = await _repository.GetPageRankAsync(projectContext, limit: 10);
            var betweenness = await _repository.GetBetweennessAsync(projectContext, limit: 10);
            var articulation = await _repository.GetArticulationPointsAsync(projectContext, limit: 10);
            var bridgeEdges = await _repository.GetBridgeEdgesAsync(projectContext, limit: 10);

            pageRank.Should().NotBeEmpty();
            betweenness.Should().Contain(item => item.Node.Id == endpoint.Id || item.Node.Id == service.Id);
            articulation.Should().Contain(item => item.Node.Id == endpoint.Id || item.Node.Id == service.Id);
            bridgeEdges.Should().Contain(edge =>
                (edge.Source.Id == entry.Id && edge.Target.Id == endpoint.Id)
                || (edge.Source.Id == endpoint.Id && edge.Target.Id == service.Id)
                || (edge.Source.Id == service.Id && edge.Target.Id == store.Id));
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task UpsertNodeAsync_WithSameSourceHash_DoesNotAdvanceContentUpdateMetadata()
    {
        var projectContext = $"Integration.SourceHash.{Guid.NewGuid():N}";
        var node = CreateNode(
            id: $"{projectContext}.Target",
            name: "Target",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Target.cs",
            namespaceName: $"{projectContext}.Target",
            lineNumber: 1,
            sourceHash: "abc123");

        try
        {
            await _repository!.UpsertNodeAsync(node);
            var first = (await _repository.QueryNodesAsync(new CodeGraphQuery
            {
                ProjectContext = projectContext,
                NameFilter = "Target",
                Limit = 10
            })).Single(match => match.Id == node.Id);

            await Task.Delay(5);
            await _repository.UpsertNodeAsync(node with { Summary = "Metadata refresh with identical source." });
            var second = (await _repository.QueryNodesAsync(new CodeGraphQuery
            {
                ProjectContext = projectContext,
                NameFilter = "Target",
                Limit = 10
            })).Single(match => match.Id == node.Id);

            second.SourceHash.Should().Be("abc123");
            second.UpdatedAt.Should().Be(first.UpdatedAt);
            second.LastIndexedAt.Should().BeAfter(first.LastIndexedAt!.Value);
            second.ChangeCount.Should().Be(first.ChangeCount);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task UpsertNodeAsync_WithDifferentSourceHash_AdvancesContentUpdateMetadata()
    {
        var projectContext = $"Integration.SourceHashChange.{Guid.NewGuid():N}";
        var node = CreateNode(
            id: $"{projectContext}.Target",
            name: "Target",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Target.cs",
            namespaceName: $"{projectContext}.Target",
            lineNumber: 1,
            sourceHash: "abc123");

        try
        {
            await _repository!.UpsertNodeAsync(node);
            var first = (await _repository.QueryNodesAsync(new CodeGraphQuery
            {
                ProjectContext = projectContext,
                NameFilter = "Target",
                Limit = 10
            })).Single(match => match.Id == node.Id);

            await Task.Delay(5);
            await _repository.UpsertNodeAsync(node with { SourceHash = "def456" });
            var second = (await _repository.QueryNodesAsync(new CodeGraphQuery
            {
                ProjectContext = projectContext,
                NameFilter = "Target",
                Limit = 10
            })).Single(match => match.Id == node.Id);

            second.SourceHash.Should().Be("def456");
            second.UpdatedAt.Should().BeAfter(first.UpdatedAt!.Value);
            second.LastIndexedAt.Should().BeAfter(first.LastIndexedAt!.Value);
            second.ChangeCount.Should().Be(first.ChangeCount + 1);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindImpactAsync_ForKnownNode_ReturnsAtLeastOneCallerOrGuidance()
    {
        var target = await FindAnyTargetAsync();
        target.Should().NotBeNull("the CodeMeridian graph should already contain indexed nodes");

        var impact = await _repository!.FindImpactAsync(target!.Id, depth: 2);

        impact.Should().NotBeNull();
    }

    [Fact]
    public async Task GetContextForEditingAsync_ForKnownNode_ReturnsTheNode()
    {
        var target = await FindAnyTargetAsync();
        target.Should().NotBeNull("the CodeMeridian graph should already contain indexed nodes");

        var context = await _repository!.GetContextForEditingAsync(target!.Id);

        context.Node.Should().NotBeNull();
        context.Node!.Id.Should().Be(target.Id);
    }

    [Fact]
    public async Task QueryEdgesAsync_ForKnownNode_ReturnsRelationships()
    {
        var target = await FindNodeWithRelationshipsAsync();
        target.Should().NotBeNull("the CodeMeridian graph should already contain indexed nodes with relationships");

        var edges = await _repository!.QueryEdgesAsync(target!.Id, depth: 1);

        edges.Should().NotBeEmpty();
        edges.Should().OnlyContain(edge =>
            !string.IsNullOrWhiteSpace(edge.SourceId)
            && !string.IsNullOrWhiteSpace(edge.TargetId));
    }

    [Fact]
    public async Task GetSubgraphSummaryAsync_ForKnownNode_ReturnsReadableSummary()
    {
        var target = await FindNodeWithRelationshipsAsync();
        target.Should().NotBeNull("the CodeMeridian graph should already contain indexed nodes with relationships");

        var summary = await _repository!.GetSubgraphSummaryAsync(target!.Id);

        summary.Should().NotBeNullOrWhiteSpace();
        summary.Should().Contain(target.Name);
        (summary.Contains("Relations", StringComparison.OrdinalIgnoreCase) || summary.Contains("File:", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
    }

    [Fact]
    public async Task FindRecentlyChangedAsync_ForRepo_ReturnsRecentNodes()
    {
        var results = await _repository!.FindRecentlyChangedAsync(
            projectContext: null,
            window: TimeSpan.FromDays(3650));

        results.Should().NotBeEmpty();
        results.Should().OnlyContain(result =>
            result.ChangedAt != default
            && !string.IsNullOrWhiteSpace(result.ChangeType));
    }

    [Fact]
    public async Task GetMostRecentCodeUpdateAsync_ForRepo_ReturnsTimestamp()
    {
        var updatedAt = await _repository!.GetMostRecentCodeUpdateAsync();

        updatedAt.Should().NotBeNull();
        updatedAt!.Value.Should().BeAfter(DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task FindDiagnosticsAsync_WithTemporaryDiagnosticFixture_ReturnsMatchingDiagnostics()
    {
        var projectContext = $"Integration.Diagnostics.{Guid.NewGuid():N}";
        var filePath = $"src/{projectContext}/Target.cs";
        var target = CreateNode(
            id: $"{projectContext}.Target",
            name: "TargetClass",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: filePath,
            namespaceName: $"{projectContext}.Target");
        var diagnostic = CreateNode(
            id: $"{projectContext}.Diag",
            name: "Error CS9999",
            type: CodeNodeType.Diagnostic,
            projectContext: projectContext,
            filePath: filePath,
            namespaceName: target.Namespace,
            lineNumber: 12,
            summary: "Synthetic diagnostic for integration coverage");

        try
        {
            await _repository!.UpsertNodeAsync(target);
            await _repository.UpsertNodeAsync(diagnostic);

            var diagnostics = await _repository.FindDiagnosticsAsync(projectContext, severity: "error");

            diagnostics.Should().ContainSingle(node => node.Id == diagnostic.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindDiagnosticsForNodeAsync_WithTemporaryDiagnosticFixture_ReturnsDiagnosticsForSameFile()
    {
        var projectContext = $"Integration.DiagnosticsForNode.{Guid.NewGuid():N}";
        var filePath = $"src/{projectContext}/Target.cs";
        var target = CreateNode(
            id: $"{projectContext}.Target",
            name: "TargetClass",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: filePath,
            namespaceName: $"{projectContext}.Target");
        var diagnostic = CreateNode(
            id: $"{projectContext}.Diag",
            name: "Warning CS1234",
            type: CodeNodeType.Diagnostic,
            projectContext: projectContext,
            filePath: filePath,
            namespaceName: target.Namespace,
            lineNumber: 21,
            summary: "Synthetic diagnostic for integration coverage");

        try
        {
            await _repository!.UpsertNodeAsync(target);
            await _repository.UpsertNodeAsync(diagnostic);

            var diagnostics = await _repository.FindDiagnosticsForNodeAsync(target.Id);

            diagnostics.Should().ContainSingle(node => node.Id == diagnostic.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindCoverageGapsAsync_WithTemporaryProductionNode_ReturnsThatNode()
    {
        var projectContext = $"Integration.Coverage.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.CoverageTarget",
            name: "CoverageTarget",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CoverageTarget.cs",
            namespaceName: $"{projectContext}.Coverage");

        try
        {
            await _repository!.UpsertNodeAsync(target);

            var gaps = await _repository.FindCoverageGapsAsync(projectContext);

            gaps.Should().ContainSingle(node => node.Id == target.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindCoverageGapsAsync_WithStoredConfigurationRole_ExcludesThatNode()
    {
        var projectContext = $"Integration.Coverage.Config.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.ConfigTarget",
            name: "ConfigTarget",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/AppConfig.ts")
        with
        {
            FileRole = IndexedFileRole.Configuration
        };

        try
        {
            await _repository!.UpsertNodeAsync(target);

            var gaps = await _repository.FindCoverageGapsAsync(projectContext);

            gaps.Should().NotContain(node => node.Id == target.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindCoverageGapsAsync_WithoutStoredFileRole_TreatsNodeAsUnknown()
    {
        var projectContext = $"Integration.Coverage.Unknown.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.ConfigTarget",
            name: "ConfigTarget",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/AppConfig.ts");

        try
        {
            await _repository!.UpsertNodeAsync(target);

            var gaps = await _repository.FindCoverageGapsAsync(projectContext);

            gaps.Should().ContainSingle(node => node.Id == target.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindRelatedTestsAsync_WithTemporaryTestFixture_ReturnsDirectTest()
    {
        var projectContext = $"Integration.RelatedTests.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.Target",
            name: "TargetMethod",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Target.cs",
            namespaceName: $"{projectContext}.Production");
        var testNode = CreateNode(
            id: $"{projectContext}.Test",
            name: "TargetMethodTests",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"tests/{projectContext}/TargetTests.cs",
            namespaceName: $"{projectContext}.Tests");
        var caller = CreateNode(
            id: $"{projectContext}.Caller",
            name: "CallerMethod",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Caller.cs",
            namespaceName: $"{projectContext}.Production");

        try
        {
            await _repository!.UpsertNodeAsync(target);
            await _repository.UpsertNodeAsync(testNode);
            await _repository.UpsertNodeAsync(caller);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = testNode.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.Calls
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = caller.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.Calls
            });

            var related = await _repository.FindRelatedTestsAsync(target.Id, projectContext);

            related.Should().ContainSingle(match =>
                match.MatchType == "direct" && match.Node.Id == testNode.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindRelatedTestsAsync_WithStoredTestRole_ReturnsDirectTestWithoutNamingHeuristics()
    {
        var projectContext = $"Integration.RelatedTests.StoredRole.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.Target",
            name: "TargetMethod",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Target.cs",
            namespaceName: $"{projectContext}.Production");
        var testNode = CreateNode(
            id: $"{projectContext}.Verifier",
            name: "Verifier",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Verifier.cs",
            namespaceName: $"{projectContext}.Verification")
        with
        {
            FileRole = IndexedFileRole.Test
        };

        try
        {
            await _repository!.UpsertNodeAsync(target);
            await _repository.UpsertNodeAsync(testNode);
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = testNode.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.Calls
            });

            var related = await _repository.FindRelatedTestsAsync(target.Id, projectContext);

            related.Should().ContainSingle(match =>
                match.MatchType == "direct" && match.Node.Id == testNode.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindHotspotsAsync_WithTemporaryFixture_ReturnsHighFanInNode()
    {
        var projectContext = $"Integration.Hotspots.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.Target",
            name: "HotspotTarget",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Target.cs",
            namespaceName: $"{projectContext}.Production");
        var callerOne = CreateNode(
            id: $"{projectContext}.CallerOne",
            name: "CallerOne",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerOne.cs",
            namespaceName: $"{projectContext}.Production");
        var callerTwo = CreateNode(
            id: $"{projectContext}.CallerTwo",
            name: "CallerTwo",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerTwo.cs",
            namespaceName: $"{projectContext}.Production");

        try
        {
            await _repository!.UpsertNodeAsync(target);
            await _repository.UpsertNodeAsync(callerOne);
            await _repository.UpsertNodeAsync(callerTwo);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerOne.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.Calls
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerTwo.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.Calls
            });

            var hotspots = await _repository.FindHotspotsAsync(projectContext, limit: 10);

            hotspots.Should().ContainSingle(item =>
                item.Node.Id == target.Id && item.FanIn >= 2);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindNaturalModuleAssignmentsAsync_WithTemporaryFixture_ReturnsRequestedCommunities()
    {
        var projectContext = $"Integration.NaturalModules.Assignments.{Guid.NewGuid():N}";
        var service = CreateNode(
            id: $"{projectContext}.OrderService",
            name: "OrderService",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrderService.cs",
            namespaceName: $"{projectContext}.Application.Orders");
        var place = CreateNode(
            id: $"{projectContext}.OrderService.PlaceOrderAsync",
            name: "PlaceOrderAsync",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: service.FilePath!,
            namespaceName: service.Namespace);
        var price = CreateNode(
            id: $"{projectContext}.OrderService.CalculatePriceAsync",
            name: "CalculatePriceAsync",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: service.FilePath!,
            namespaceName: service.Namespace);
        var repository = CreateNode(
            id: $"{projectContext}.IOrderRepository",
            name: "IOrderRepository",
            type: CodeNodeType.Interface,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/IOrderRepository.cs",
            namespaceName: $"{projectContext}.Application.Orders");
        var endpoint = CreateNode(
            id: $"{projectContext}.OrdersEndpoint",
            name: "POST /api/orders",
            type: CodeNodeType.ApiEndpoint,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrdersEndpoint.cs",
            namespaceName: $"{projectContext}.Api");
        var other = CreateNode(
            id: $"{projectContext}.EmailSender",
            name: "EmailSender",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/EmailSender.cs",
            namespaceName: $"{projectContext}.Application.Notifications");

        try
        {
            await _repository!.UpsertNodeAsync(service);
            await _repository.UpsertNodeAsync(place);
            await _repository.UpsertNodeAsync(price);
            await _repository.UpsertNodeAsync(repository);
            await _repository.UpsertNodeAsync(endpoint);
            await _repository.UpsertNodeAsync(other);

            await _repository.UpsertEdgeAsync(new CodeEdge { SourceId = service.Id, TargetId = place.Id, Type = CodeEdgeType.Contains });
            await _repository.UpsertEdgeAsync(new CodeEdge { SourceId = service.Id, TargetId = price.Id, Type = CodeEdgeType.Contains });
            await _repository.UpsertEdgeAsync(new CodeEdge { SourceId = endpoint.Id, TargetId = place.Id, Type = CodeEdgeType.Calls });
            await _repository.UpsertEdgeAsync(new CodeEdge { SourceId = endpoint.Id, TargetId = price.Id, Type = CodeEdgeType.Calls });
            await _repository.UpsertEdgeAsync(new CodeEdge { SourceId = place.Id, TargetId = repository.Id, Type = CodeEdgeType.DependsOn });
            await _repository.UpsertEdgeAsync(new CodeEdge { SourceId = price.Id, TargetId = repository.Id, Type = CodeEdgeType.DependsOn });

            var assignments = await _repository.FindNaturalModuleAssignmentsAsync(
                [place.Id, price.Id, repository.Id, endpoint.Id, other.Id],
                projectContext);
            var byId = assignments.ToDictionary(item => item.Node.Id, item => item.Community, StringComparer.Ordinal);

            byId.Should().ContainKey(place.Id);
            byId.Should().ContainKey(price.Id);
            byId.Should().ContainKey(repository.Id);
            byId.Should().ContainKey(endpoint.Id);
            byId[place.Id].Should().Be(byId[price.Id]);
            byId[place.Id].Should().Be(byId[repository.Id]);
            byId[place.Id].Should().Be(byId[endpoint.Id]);
            byId[other.Id].Should().NotBe(byId[place.Id]);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindGodClassesAsync_WithLargeClassAndHighFanIn_ReturnsThatClass()
    {
        var projectContext = $"Integration.GodClasses.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.OrderWorkflow",
            name: "OrderWorkflow",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrderWorkflow.cs",
            namespaceName: $"{projectContext}.Production")
        with
        {
            LineCount = 360
        };
        var targetMember = CreateNode(
            id: $"{projectContext}.OrderWorkflow.Run",
            name: "OrderWorkflow.Run",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrderWorkflow.cs",
            namespaceName: $"{projectContext}.Production")
        with
        {
            LineCount = 40
        };
        var callerOne = CreateNode(
            id: $"{projectContext}.CallerOne",
            name: "CallerOne",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerOne.cs",
            namespaceName: $"{projectContext}.Production");
        var callerTwo = CreateNode(
            id: $"{projectContext}.CallerTwo",
            name: "CallerTwo",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerTwo.cs",
            namespaceName: $"{projectContext}.Production");
        var callerThree = CreateNode(
            id: $"{projectContext}.CallerThree",
            name: "CallerThree",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerThree.cs",
            namespaceName: $"{projectContext}.Production");
        var callerFour = CreateNode(
            id: $"{projectContext}.CallerFour",
            name: "CallerFour",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerFour.cs",
            namespaceName: $"{projectContext}.Production");

        try
        {
            await _repository!.UpsertNodeAsync(target);
            await _repository.UpsertNodeAsync(targetMember);
            await _repository.UpsertNodeAsync(callerOne);
            await _repository.UpsertNodeAsync(callerTwo);
            await _repository.UpsertNodeAsync(callerThree);
            await _repository.UpsertNodeAsync(callerFour);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = target.Id,
                TargetId = targetMember.Id,
                Type = CodeEdgeType.Contains
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerOne.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.DependsOn
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerTwo.Id,
                TargetId = targetMember.Id,
                Type = CodeEdgeType.Calls
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerThree.Id,
                TargetId = targetMember.Id,
                Type = CodeEdgeType.Uses
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerFour.Id,
                TargetId = targetMember.Id,
                Type = CodeEdgeType.DependsOn
            });

            var results = await _repository.FindGodClassesAsync(projectContext, lineThreshold: 300, fanInThreshold: 3);

            results.Should().ContainSingle(item =>
                item.Node.Id == target.Id &&
                item.LineCount == 360 &&
                item.FanIn >= 4);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindLargeNodesAsync_WithoutStoredFileRole_IncludesConfigurationNamedNodeAsUnknown()
    {
        var projectContext = $"Integration.LargeNodes.Unknown.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.ConfigTarget",
            name: "ConfigTarget",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/AppConfig.ts")
        with
        {
            LineCount = 450
        };

        try
        {
            await _repository!.UpsertNodeAsync(target);

            var results = await _repository.FindLargeNodesAsync(projectContext, classThreshold: 300, methodThreshold: 40);

            results.Should().ContainSingle(node => node.Id == target.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindGodClassesAsync_WithStoredConfigurationRole_ExcludesThatClass()
    {
        var projectContext = $"Integration.GodClasses.Config.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.AppConfig",
            name: "AppConfig",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/AppConfig.ts")
        with
        {
            LineCount = 360,
            FileRole = IndexedFileRole.Configuration
        };
        var targetMember = CreateNode(
            id: $"{projectContext}.AppConfig.Load",
            name: "AppConfig.Load",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/AppConfig.ts")
        with
        {
            LineCount = 40,
            FileRole = IndexedFileRole.Configuration
        };
        var callerOne = CreateNode(
            id: $"{projectContext}.CallerOne",
            name: "CallerOne",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerOne.cs");
        var callerTwo = CreateNode(
            id: $"{projectContext}.CallerTwo",
            name: "CallerTwo",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerTwo.cs");
        var callerThree = CreateNode(
            id: $"{projectContext}.CallerThree",
            name: "CallerThree",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerThree.cs");
        var callerFour = CreateNode(
            id: $"{projectContext}.CallerFour",
            name: "CallerFour",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerFour.cs");

        try
        {
            await _repository!.UpsertNodeAsync(target);
            await _repository.UpsertNodeAsync(targetMember);
            await _repository.UpsertNodeAsync(callerOne);
            await _repository.UpsertNodeAsync(callerTwo);
            await _repository.UpsertNodeAsync(callerThree);
            await _repository.UpsertNodeAsync(callerFour);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = target.Id,
                TargetId = targetMember.Id,
                Type = CodeEdgeType.Contains
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerOne.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.DependsOn
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerTwo.Id,
                TargetId = targetMember.Id,
                Type = CodeEdgeType.Calls
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerThree.Id,
                TargetId = targetMember.Id,
                Type = CodeEdgeType.Uses
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerFour.Id,
                TargetId = targetMember.Id,
                Type = CodeEdgeType.DependsOn
            });

            var results = await _repository.FindGodClassesAsync(projectContext, lineThreshold: 300, fanInThreshold: 3);

            results.Should().NotContain(item => item.Node.Id == target.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindGodClassesAsync_DistinguishesFanInPerCandidateClass()
    {
        var projectContext = $"Integration.GodClasses.FanIn.{Guid.NewGuid():N}";
        var primary = CreateNode(
            id: $"{projectContext}.Primary",
            name: "PrimaryService",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/PrimaryService.cs")
        with
        {
            LineCount = 360
        };
        var primaryMember = CreateNode(
            id: $"{projectContext}.Primary.Run",
            name: "PrimaryService.Run",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/PrimaryService.cs");
        var secondary = CreateNode(
            id: $"{projectContext}.Secondary",
            name: "SecondaryService",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/SecondaryService.cs")
        with
        {
            LineCount = 420
        };
        var secondaryMember = CreateNode(
            id: $"{projectContext}.Secondary.Run",
            name: "SecondaryService.Run",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/SecondaryService.cs");
        var primaryCallers = Enumerable.Range(1, 4)
            .Select(index => CreateNode(
                id: $"{projectContext}.PrimaryCaller{index}",
                name: $"PrimaryCaller{index}",
                type: CodeNodeType.Method,
                projectContext: projectContext,
                filePath: $"src/{projectContext}/PrimaryCaller{index}.cs"))
            .ToArray();
        var secondaryCaller = CreateNode(
            id: $"{projectContext}.SecondaryCaller",
            name: "SecondaryCaller",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/SecondaryCaller.cs");

        try
        {
            await _repository!.UpsertNodeAsync(primary);
            await _repository.UpsertNodeAsync(primaryMember);
            await _repository.UpsertNodeAsync(secondary);
            await _repository.UpsertNodeAsync(secondaryMember);
            foreach (var caller in primaryCallers)
                await _repository.UpsertNodeAsync(caller);
            await _repository.UpsertNodeAsync(secondaryCaller);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = primary.Id,
                TargetId = primaryMember.Id,
                Type = CodeEdgeType.Contains
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = secondary.Id,
                TargetId = secondaryMember.Id,
                Type = CodeEdgeType.Contains
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = primaryCallers[0].Id,
                TargetId = primary.Id,
                Type = CodeEdgeType.DependsOn
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = primaryCallers[1].Id,
                TargetId = primaryMember.Id,
                Type = CodeEdgeType.Calls
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = primaryCallers[2].Id,
                TargetId = primaryMember.Id,
                Type = CodeEdgeType.Uses
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = primaryCallers[3].Id,
                TargetId = primaryMember.Id,
                Type = CodeEdgeType.DependsOn
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = secondaryCaller.Id,
                TargetId = secondary.Id,
                Type = CodeEdgeType.Calls
            });

            var results = await _repository.FindGodClassesAsync(projectContext, lineThreshold: 300, fanInThreshold: 0);

            results.Should().Contain(item =>
                item.Node.Id == primary.Id &&
                item.FanIn == 4);
            results.Should().Contain(item =>
                item.Node.Id == secondary.Id &&
                item.FanIn == 1);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindConnectionAsync_WithTemporaryFixture_ReturnsShortestPath()
    {
        var projectContext = $"Integration.Connection.{Guid.NewGuid():N}";
        var source = CreateNode(
            id: $"{projectContext}.Source",
            name: "SourceMethod",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Source.cs",
            namespaceName: $"{projectContext}.Production");
        var middle = CreateNode(
            id: $"{projectContext}.Middle",
            name: "MiddleMethod",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Middle.cs",
            namespaceName: $"{projectContext}.Production");
        var target = CreateNode(
            id: $"{projectContext}.Target",
            name: "TargetMethod",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Target.cs",
            namespaceName: $"{projectContext}.Production");

        try
        {
            await _repository!.UpsertNodeAsync(source);
            await _repository.UpsertNodeAsync(middle);
            await _repository.UpsertNodeAsync(target);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = source.Id,
                TargetId = middle.Id,
                Type = CodeEdgeType.Calls
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = middle.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.Calls
            });

            var connection = await _repository.FindConnectionAsync(source.Id, target.Id);

            connection.Should().HaveCount(3);
            connection[0].Node.Id.Should().Be(source.Id);
            connection[0].ViaRelationship.Should().Be("Calls");
            connection[1].Node.Id.Should().Be(middle.Id);
            connection[1].ViaRelationship.Should().Be("Calls");
            connection[2].Node.Id.Should().Be(target.Id);
            connection[2].ViaRelationship.Should().BeNull();
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindConnectionAsync_WithFrontendFixture_ReturnsFrontendRelationshipPath()
    {
        var projectContext = $"Integration.Connection.Frontend.{Guid.NewGuid():N}";
        var component = CreateNode(
            id: $"{projectContext}.Component",
            name: "HeroCard.tsx",
            type: CodeNodeType.File,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/HeroCard.tsx",
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["frontendRole"] = "ComponentFile"
            });
        var classConcept = CreateNode(
            id: $"{projectContext}.CssClass.hero",
            name: "hero",
            type: CodeNodeType.ExternalConcept,
            projectContext: projectContext,
            filePath: string.Empty,
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "CssClass"
            });
        var selector = CreateNode(
            id: $"{projectContext}.CssSelector.hero",
            name: ".hero",
            type: CodeNodeType.ExternalConcept,
            projectContext: projectContext,
            filePath: string.Empty,
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "CssSelector"
            });
        var stylesheet = CreateNode(
            id: $"{projectContext}.Stylesheet",
            name: "HeroCard.scss",
            type: CodeNodeType.File,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/HeroCard.scss",
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["frontendRole"] = "StyleSheetFile"
            });

        try
        {
            await _repository!.UpsertNodeAsync(component);
            await _repository.UpsertNodeAsync(classConcept);
            await _repository.UpsertNodeAsync(selector);
            await _repository.UpsertNodeAsync(stylesheet);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = component.Id,
                TargetId = classConcept.Id,
                Type = CodeEdgeType.UsesClass
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = selector.Id,
                TargetId = classConcept.Id,
                Type = CodeEdgeType.UsesClass
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = stylesheet.Id,
                TargetId = selector.Id,
                Type = CodeEdgeType.DefinesSelector
            });

            var connection = await _repository.FindConnectionAsync(component.Id, stylesheet.Id);

            connection.Should().HaveCount(4);
            connection[0].Node.Id.Should().Be(component.Id);
            connection[0].ViaRelationship.Should().Be("UsesClass");
            connection[1].Node.Id.Should().Be(classConcept.Id);
            connection[1].ViaRelationship.Should().Be("UsesClass");
            connection[2].Node.Id.Should().Be(selector.Id);
            connection[2].ViaRelationship.Should().Be("DefinesSelector");
            connection[3].Node.Id.Should().Be(stylesheet.Id);
            connection[3].ViaRelationship.Should().BeNull();
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindConnectionAsync_WithDatabaseFixture_ReturnsDatabaseOperationAndTablePath()
    {
        var projectContext = $"Integration.Connection.Database.{Guid.NewGuid():N}";
        var endpoint = CreateNode(
            id: $"{projectContext}.Endpoint",
            name: "POST /api/orders",
            type: CodeNodeType.ApiEndpoint,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrdersEndpoint.cs");
        var handler = CreateNode(
            id: $"{projectContext}.Handler",
            name: "CreateOrder",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrdersEndpoint.cs",
            namespaceName: $"{projectContext}.Api");
        var operation = CreateNode(
            id: $"{projectContext}.DbOperation",
            name: "EFCore Writes Orders",
            type: CodeNodeType.ExternalConcept,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrdersRepository.cs",
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "DatabaseOperation",
                ["provider"] = "EFCore"
            });
        var table = CreateNode(
            id: $"{projectContext}.OrdersTable",
            name: "Orders",
            type: CodeNodeType.DatabaseTable,
            projectContext: projectContext,
            filePath: string.Empty,
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "DatabaseTable"
            });

        try
        {
            await _repository!.UpsertNodeAsync(endpoint);
            await _repository.UpsertNodeAsync(handler);
            await _repository.UpsertNodeAsync(operation);
            await _repository.UpsertNodeAsync(table);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = handler.Id,
                TargetId = endpoint.Id,
                Type = CodeEdgeType.Uses
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = handler.Id,
                TargetId = operation.Id,
                Type = CodeEdgeType.Writes
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = operation.Id,
                TargetId = table.Id,
                Type = CodeEdgeType.Writes
            });

            var connection = await _repository.FindConnectionAsync(endpoint.Id, table.Id);

            connection.Should().HaveCount(4);
            connection[0].Node.Id.Should().Be(endpoint.Id);
            connection[0].ViaRelationship.Should().Be("Uses");
            connection[1].Node.Id.Should().Be(handler.Id);
            connection[1].ViaRelationship.Should().Be("Writes");
            connection[2].Node.Id.Should().Be(operation.Id);
            connection[2].ViaRelationship.Should().Be("Writes");
            connection[3].Node.Id.Should().Be(table.Id);
            connection[3].ViaRelationship.Should().BeNull();
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindEndpointTracesAsync_WithDatabaseAndMessageFixture_ReturnsTerminalPaths()
    {
        var projectContext = $"Integration.TraceEndpoint.{Guid.NewGuid():N}";
        var endpoint = CreateNode(
            id: $"{projectContext}.Endpoint",
            name: "POST /api/orders",
            type: CodeNodeType.ApiEndpoint,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrdersEndpoint.cs");
        var handler = CreateNode(
            id: $"{projectContext}.Handler",
            name: "CreateOrder",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrdersEndpoint.cs",
            namespaceName: $"{projectContext}.Api");
        var operation = CreateNode(
            id: $"{projectContext}.DbOperation",
            name: "EFCore Writes Orders",
            type: CodeNodeType.ExternalConcept,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrdersRepository.cs",
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "DatabaseOperation",
                ["provider"] = "EFCore"
            });
        var table = CreateNode(
            id: $"{projectContext}.OrdersTable",
            name: "Orders",
            type: CodeNodeType.DatabaseTable,
            projectContext: projectContext,
            filePath: string.Empty,
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "DatabaseTable"
            });
        var topic = CreateNode(
            id: $"{projectContext}.OrderCreated",
            name: "order-created",
            type: CodeNodeType.MessageTopic,
            projectContext: projectContext,
            filePath: string.Empty);

        try
        {
            await _repository!.UpsertNodeAsync(endpoint);
            await _repository.UpsertNodeAsync(handler);
            await _repository.UpsertNodeAsync(operation);
            await _repository.UpsertNodeAsync(table);
            await _repository.UpsertNodeAsync(topic);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = handler.Id,
                TargetId = endpoint.Id,
                Type = CodeEdgeType.Uses
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = handler.Id,
                TargetId = operation.Id,
                Type = CodeEdgeType.Writes
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = operation.Id,
                TargetId = table.Id,
                Type = CodeEdgeType.Writes
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = handler.Id,
                TargetId = topic.Id,
                Type = CodeEdgeType.PublishesTo
            });

            var traces = await _repository.FindEndpointTracesAsync("POST /api/orders", projectContext);

            traces.Select(path => path.Steps.Last().Node.Id).Should().Contain(table.Id);
            traces.Select(path => path.Steps.Last().Node.Id).Should().Contain(topic.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindImpactAsync_WithFrontendConceptFixture_ReturnsMarkupAndStylesheetDependents()
    {
        var projectContext = $"Integration.Impact.Frontend.{Guid.NewGuid():N}";
        var classConcept = CreateNode(
            id: $"{projectContext}.CssClass.hero",
            name: "hero",
            type: CodeNodeType.ExternalConcept,
            projectContext: projectContext,
            filePath: string.Empty,
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "CssClass"
            });
        var component = CreateNode(
            id: $"{projectContext}.Component",
            name: "HeroCard.tsx",
            type: CodeNodeType.File,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/HeroCard.tsx",
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["frontendRole"] = "ComponentFile"
            });
        var selector = CreateNode(
            id: $"{projectContext}.CssSelector.hero",
            name: ".hero",
            type: CodeNodeType.ExternalConcept,
            projectContext: projectContext,
            filePath: string.Empty,
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "CssSelector"
            });
        var stylesheet = CreateNode(
            id: $"{projectContext}.Stylesheet",
            name: "HeroCard.scss",
            type: CodeNodeType.File,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/HeroCard.scss",
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["frontendRole"] = "StyleSheetFile"
            });

        try
        {
            await _repository!.UpsertNodeAsync(classConcept);
            await _repository.UpsertNodeAsync(component);
            await _repository.UpsertNodeAsync(selector);
            await _repository.UpsertNodeAsync(stylesheet);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = component.Id,
                TargetId = classConcept.Id,
                Type = CodeEdgeType.UsesClass
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = selector.Id,
                TargetId = classConcept.Id,
                Type = CodeEdgeType.UsesClass
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = stylesheet.Id,
                TargetId = selector.Id,
                Type = CodeEdgeType.DefinesSelector
            });

            var impact = await _repository.FindImpactAsync(classConcept.Id, depth: 3);

            impact.Should().Contain(item => item.Node.Id == component.Id && item.Distance == 1);
            impact.Should().Contain(item => item.Node.Id == selector.Id && item.Distance == 1);
            impact.Should().Contain(item => item.Node.Id == stylesheet.Id && item.Distance == 2);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task GetContextForEditingAsync_WithFrontendFixture_ReturnsImportingFilesAndDefinedSelectors()
    {
        var projectContext = $"Integration.Context.Frontend.{Guid.NewGuid():N}";
        var stylesheet = CreateNode(
            id: $"{projectContext}.Stylesheet",
            name: "HeroCard.scss",
            type: CodeNodeType.File,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/HeroCard.scss",
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["frontendRole"] = "StyleSheetFile"
            });
        var component = CreateNode(
            id: $"{projectContext}.Component",
            name: "HeroCard.tsx",
            type: CodeNodeType.File,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/HeroCard.tsx",
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["frontendRole"] = "ComponentFile"
            });
        var selector = CreateNode(
            id: $"{projectContext}.CssSelector.hero",
            name: ".hero",
            type: CodeNodeType.ExternalConcept,
            projectContext: projectContext,
            filePath: string.Empty,
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "CssSelector"
            });

        try
        {
            await _repository!.UpsertNodeAsync(stylesheet);
            await _repository.UpsertNodeAsync(component);
            await _repository.UpsertNodeAsync(selector);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = component.Id,
                TargetId = stylesheet.Id,
                Type = CodeEdgeType.ImportsStyle
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = stylesheet.Id,
                TargetId = selector.Id,
                Type = CodeEdgeType.DefinesSelector
            });

            var context = await _repository.GetContextForEditingAsync(stylesheet.Id);

            context.Node.Should().NotBeNull();
            context.Callers.Should().Contain(node => node.Id == component.Id);
            context.Callees.Should().Contain(node => node.Id == selector.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindCrossProjectDependenciesAsync_WithTemporaryFixtures_ReturnsCrossProjectEdge()
    {
        var sourceProject = $"Integration.CrossProject.Source.{Guid.NewGuid():N}";
        var targetProject = $"Integration.CrossProject.Target.{Guid.NewGuid():N}";
        var source = CreateNode(
            id: $"{sourceProject}.Source",
            name: "SourceMethod",
            type: CodeNodeType.Method,
            projectContext: sourceProject,
            filePath: $"src/{sourceProject}/Source.cs",
            namespaceName: $"{sourceProject}.Production");
        var target = CreateNode(
            id: $"{targetProject}.Target",
            name: "TargetMethod",
            type: CodeNodeType.Method,
            projectContext: targetProject,
            filePath: $"src/{targetProject}/Target.cs",
            namespaceName: $"{targetProject}.Production");

        try
        {
            await _repository!.UpsertNodeAsync(source);
            await _repository.UpsertNodeAsync(target);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = source.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.Calls
            });

            var dependencies = await _repository.FindCrossProjectDependenciesAsync();

            dependencies.Should().ContainSingle(item =>
                item.Source.Id == source.Id
                && item.Target.Id == target.Id
                && item.RelationshipType == "Calls");
        }
        finally
        {
            await _repository!.DeleteProjectAsync(sourceProject);
            await _repository.DeleteProjectAsync(targetProject);
        }
    }

    [Fact]
    public async Task FindUnreferencedAsync_WithTemporaryFixture_ReturnsIsolatedNode()
    {
        var projectContext = $"Integration.Unreferenced.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.Target",
            name: "UnreferencedClass",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Target.cs",
            namespaceName: $"{projectContext}.Production");

        try
        {
            await _repository!.UpsertNodeAsync(target);

            var unreferenced = await _repository.FindUnreferencedAsync(projectContext);

            unreferenced.Should().ContainSingle(node => node.Id == target.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindSmellPathsAsync_WithCoreToInfrastructurePath_ReturnsShortestPath()
    {
        var projectContext = $"Integration.SmellPaths.{Guid.NewGuid():N}";
        var source = CreateNode(
            id: $"{projectContext}.Core.PricingRules",
            name: "PricingRules",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Core/PricingRules.cs",
            namespaceName: $"{projectContext}.Core");
        var middle = CreateNode(
            id: $"{projectContext}.Application.OrderWorkflow",
            name: "OrderWorkflow",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Application/OrderWorkflow.cs",
            namespaceName: $"{projectContext}.Application");
        var target = CreateNode(
            id: $"{projectContext}.Infrastructure.Neo4jOrderStore",
            name: "Neo4jOrderStore",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Infrastructure/Neo4jOrderStore.cs",
            namespaceName: $"{projectContext}.Infrastructure");

        try
        {
            await _repository!.UpsertNodeAsync(source);
            await _repository.UpsertNodeAsync(middle);
            await _repository.UpsertNodeAsync(target);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = source.Id,
                TargetId = middle.Id,
                Type = CodeEdgeType.Uses
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = middle.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.DependsOn
            });

            var results = await _repository.FindSmellPathsAsync(projectContext, maxDepth: 4);

            results.Should().ContainSingle(path =>
                path.Source.Id == source.Id
                && path.Target.Id == target.Id
                && path.Distance == 2
                && path.Violation.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase)
                && path.Steps.Count == 3
                && path.Steps[0].Node.Id == source.Id
                && path.Steps[1].Node.Id == middle.Id
                && path.Steps[2].Node.Id == target.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindArchitectureViolationsAsync_WithIndexedArchitectureConfig_UsesConfiguredLayers()
    {
        var projectContext = $"Integration.ArchitectureConfig.{Guid.NewGuid():N}";
        var source = CreateNode(
            id: $"{projectContext}.Domain.OrderRules",
            name: "OrderRules",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Domain/OrderRules.cs",
            namespaceName: $"{projectContext}.Domain");
        var target = CreateNode(
            id: $"{projectContext}.Persistence.SqlOrderStore",
            name: "SqlOrderStore",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Persistence/SqlOrderStore.cs",
            namespaceName: $"{projectContext}.Persistence");

        try
        {
            await _repository!.UpsertNodeAsync(source);
            await _repository.UpsertNodeAsync(target);
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = source.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.DependsOn
            });

            await IngestArchitectureConfigAsync(
                projectContext,
                ".meridian/architecture.json",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["name"] = "Custom Onion",
                    ["layers:0:id"] = "Domain",
                    ["layers:0:namespaceContainsAny:0"] = ".Domain",
                    ["layers:1:id"] = "Persistence",
                    ["layers:1:namespaceContainsAny:0"] = ".Persistence",
                    ["forbiddenDependencies:0:from"] = "Domain",
                    ["forbiddenDependencies:0:to"] = "Persistence",
                    ["forbiddenDependencies:0:reason"] = "Domain must not depend on Persistence"
                });

            var violations = await _repository.FindArchitectureViolationsAsync(projectContext);

            violations.Should().ContainSingle(item =>
                item.Source.Id == source.Id
                && item.Target.Id == target.Id
                && item.Violation == "Domain must not depend on Persistence");
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    private async Task<CodeNode?> FindAnyTargetAsync()
    {
        var matches = await _repository!.QueryNodesAsync(
            new CodeGraphQuery
            {
                ProjectContext = null,
                Limit = 50
            });

        return matches.FirstOrDefault(node =>
            node.Type is CodeNodeType.Class or CodeNodeType.Method or CodeNodeType.File
            && !string.IsNullOrWhiteSpace(node.FilePath));
    }

    private async Task<CodeNode?> FindNodeWithRelationshipsAsync()
    {
        var matches = await _repository!.QueryNodesAsync(
            new CodeGraphQuery
            {
                ProjectContext = null,
                Limit = 100
            });

        foreach (var node in matches.Where(node => !string.IsNullOrWhiteSpace(node.FilePath)))
        {
            var edges = await _repository.QueryEdgesAsync(node.Id, depth: 1);
            if (edges.Count > 0)
                return node;
        }

        return null;
    }

    private static CodeNode CreateNode(
        string id,
        string name,
        CodeNodeType type,
        string projectContext,
        string filePath,
        string? namespaceName = null,
        int? lineNumber = null,
        string? summary = null,
        string? sourceHash = null,
        float[]? embedding = null,
        Dictionary<string, string>? properties = null,
        IndexedFileRole fileRole = IndexedFileRole.Unknown)
    {
        return new CodeNode
        {
            Id = id,
            Name = name,
            Type = type,
            ProjectContext = projectContext,
            FilePath = filePath,
            Namespace = namespaceName,
            LineNumber = lineNumber,
            Summary = summary,
            SourceHash = sourceHash,
            Embedding = embedding,
            FileRole = fileRole,
            Properties = properties ?? []
        };
    }

    private async Task IngestArchitectureConfigAsync(
        string projectContext,
        string architecturePath,
        IReadOnlyDictionary<string, string> entries)
    {
        var meridianFile = CreateNode(
            id: $"{projectContext}::ConfigurationFile::meridian.json",
            name: "meridian.json",
            type: CodeNodeType.ConfigurationFile,
            projectContext: projectContext,
            filePath: "meridian.json");
        var architecturePathKey = CreateNode(
            id: $"{projectContext}::ConfigurationKey::architecture:path",
            name: "architecture:path",
            type: CodeNodeType.ConfigurationKey,
            projectContext: projectContext,
            filePath: "meridian.json");
        var architecturePathEntry = CreateNode(
            id: $"{projectContext}::ConfigurationEntry::meridian.json::architecture-path",
            name: "architecture:path",
            type: CodeNodeType.ConfigurationEntry,
            projectContext: projectContext,
            filePath: "meridian.json",
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["canonicalKey"] = "architecture:path",
                ["rawValuePreview"] = architecturePath
            });
        var architectureFile = CreateNode(
            id: $"{projectContext}::ConfigurationFile::{architecturePath}",
            name: Path.GetFileName(architecturePath),
            type: CodeNodeType.ConfigurationFile,
            projectContext: projectContext,
            filePath: architecturePath);

        await _repository!.UpsertNodeAsync(meridianFile);
        await _repository.UpsertNodeAsync(architecturePathKey);
        await _repository.UpsertNodeAsync(architecturePathEntry);
        await _repository.UpsertNodeAsync(architectureFile);

        await _repository.UpsertEdgeAsync(new CodeEdge
        {
            SourceId = meridianFile.Id,
            TargetId = architecturePathEntry.Id,
            Type = CodeEdgeType.DefinesConfig
        });
        await _repository.UpsertEdgeAsync(new CodeEdge
        {
            SourceId = architecturePathEntry.Id,
            TargetId = architecturePathKey.Id,
            Type = CodeEdgeType.DefinesConfig,
            Properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["valuePreview"] = architecturePath
            }
        });

        var index = 0;
        foreach (var entry in entries)
        {
            var keyId = $"{projectContext}::ConfigurationKey::{entry.Key}";
            var entryId = $"{projectContext}::ConfigurationEntry::{architecturePath}::{index++}";
            await _repository.UpsertNodeAsync(CreateNode(
                keyId,
                entry.Key,
                CodeNodeType.ConfigurationKey,
                projectContext,
                architecturePath));
            await _repository.UpsertNodeAsync(CreateNode(
                entryId,
                entry.Key,
                CodeNodeType.ConfigurationEntry,
                projectContext,
                architecturePath,
                properties: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["canonicalKey"] = entry.Key,
                    ["rawValuePreview"] = entry.Value
                }));
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = architectureFile.Id,
                TargetId = entryId,
                Type = CodeEdgeType.DefinesConfig
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = entryId,
                TargetId = keyId,
                Type = CodeEdgeType.DefinesConfig,
                Properties = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["valuePreview"] = entry.Value
                }
            });
        }
    }
}
