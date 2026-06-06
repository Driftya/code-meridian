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
        string? sourceHash = null)
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
            SourceHash = sourceHash
        };
    }
}
