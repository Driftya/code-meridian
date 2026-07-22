using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceToolDependencyImpactTests
{
    public static TheoryData<string, string, string, bool> CatalogEdges =>
        ToolDependencyCatalog.Edges.Aggregate(
            new TheoryData<string, string, string, bool>(),
            (data, edge) =>
            {
                data.Add(edge.ProducerId, edge.ConsumerId, edge.ContractType, edge.ImpactLevel.Equals("awareness", StringComparison.OrdinalIgnoreCase));
                return data;
            });

    [Fact]
    public async Task FindToolDependencyImpactAsync_WithoutSubject_ReturnsMatrixSummary()
    {
        var sut = BuildService();

        var result = await sut.FindToolDependencyImpactAsync();

        result.Should().Contain("## Tool Dependency Impact Matrix");
        result.Should().Contain("`plan_context_workflow`");
        result.Should().Contain("`execute_context_workflow`");
        result.Should().Contain("Awareness-only edges hidden by default");
        result.Should().NotContain("`find_related_knowledge` | `pr_context_report`");
    }

    [Fact]
    public async Task FindToolDependencyImpactAsync_WithKnownSubject_ReturnsUpstreamAndDownstreamDetails()
    {
        var sut = BuildService();

        var result = await sut.FindToolDependencyImpactAsync("mcp__CodeMeridian.build_minimal_context");

        result.Should().Contain("## Tool Dependency Impact - `build_minimal_context`");
        result.Should().Contain("### Upstream Dependencies");
        result.Should().Contain("`find_test_shield`");
        result.Should().Contain("### Downstream Consumers");
        result.Should().Contain("`evaluate_session`");
        result.Should().Contain("`tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceBuildMinimalContextTests.cs`");
        result.Should().Contain("`tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceFindTestShieldTests.cs`");
        result.Should().NotContain("No tracked dependencies.");
    }

    [Fact]
    public async Task FindToolDependencyImpactAsync_WithAwarenessEnabled_IncludesSofterEdges()
    {
        var sut = BuildService();

        var result = await sut.FindToolDependencyImpactAsync("find_test_shield", includeAwarenessOnly: true);

        result.Should().Contain("`pr_context_report`");
        result.Should().Contain("`plan_context_workflow`");
        result.Should().Contain("Awareness");
    }

    [Fact]
    public async Task FindToolDependencyImpactAsync_WithPlanningSubject_ShowsLexicalAndShieldAwarenessDependencies()
    {
        var sut = BuildService();

        var result = await sut.FindToolDependencyImpactAsync("plan_context_workflow", includeAwarenessOnly: true);

        result.Should().Contain("## Tool Dependency Impact - `plan_context_workflow`");
        result.Should().Contain("### Upstream Dependencies");
        result.Should().Contain("`find_test_shield`");
        result.Should().Contain("`find_related_knowledge`");
        result.Should().Contain("### Downstream Consumers");
        result.Should().Contain("`execute_context_workflow`");
    }

    [Fact]
    public async Task FindToolDependencyImpactAsync_WithPlanEditRouteSubject_ShowsTargetingAndContextDependencies()
    {
        var sut = BuildService();

        var result = await sut.FindToolDependencyImpactAsync("plan_edit_route", includeAwarenessOnly: true);

        result.Should().Contain("## Tool Dependency Impact - `plan_edit_route`");
        result.Should().Contain("### Upstream Dependencies");
        result.Should().Contain("`find_implementation_surface`");
        result.Should().Contain("`build_minimal_context`");
        result.Should().Contain("### Review Artifacts");
        result.Should().Contain("`docs/features/24-add-change-route-planning.md`");
    }

    [Fact]
    public async Task FindToolDependencyImpactAsync_WithResolveExactSymbolSubject_ShowsImplementationSurfaceHandoff()
    {
        var sut = BuildService();

        var result = await sut.FindToolDependencyImpactAsync("resolve_exact_symbol", includeAwarenessOnly: true);

        result.Should().Contain("## Tool Dependency Impact - `resolve_exact_symbol`");
        result.Should().Contain("### Upstream Dependencies");
        result.Should().Contain("`find_implementation_surface`");
        result.Should().Contain("docs/features/14-improve-exact-symbol-resolution.md");
    }

    [Fact]
    public async Task FindToolDependencyImpactAsync_WithSuggestExtractionsSubject_ShowsPlannerAwareness()
    {
        var sut = BuildService();

        var result = await sut.FindToolDependencyImpactAsync("suggest_extractions", includeAwarenessOnly: true);

        result.Should().Contain("## Tool Dependency Impact - `suggest_extractions`");
        result.Should().Contain("### Downstream Consumers");
        result.Should().Contain("`plan_context_workflow`");
        result.Should().Contain("docs/features/29-add-refactor-extraction-candidates.md");
    }

    [Fact]
    public async Task FindToolDependencyImpactAsync_WithUnknownSubject_ReturnsGuidance()
    {
        var sut = BuildService();

        var result = await sut.FindToolDependencyImpactAsync("not_a_real_tool");

        result.Should().Contain("No tracked tool dependency subject matched `not_a_real_tool`.");
        result.Should().Contain("`find_test_shield`");
        result.Should().Contain("`suggest_extractions`");
        result.Should().Contain("`evaluate_session`");
    }

    [Theory]
    [MemberData(nameof(CatalogEdges))]
    public async Task FindToolDependencyImpactAsync_ForEachCatalogEdge_ListsConsumerFromProducerView(
        string producerId,
        string consumerId,
        string contractType,
        bool awarenessOnly)
    {
        var sut = BuildService();

        var result = await sut.FindToolDependencyImpactAsync(producerId, includeAwarenessOnly: true);

        result.Should().Contain($"## Tool Dependency Impact - `{producerId}`");
        result.Should().Contain("### Downstream Consumers");
        result.Should().Contain($"`{consumerId}`");
        result.Should().Contain($"Contract: {contractType}");
        result.Should().Contain(awarenessOnly ? "Awareness" : "Hard");
    }

    [Theory]
    [MemberData(nameof(CatalogEdges))]
    public async Task FindToolDependencyImpactAsync_ForEachCatalogEdge_ListsProducerFromConsumerView(
        string producerId,
        string consumerId,
        string contractType,
        bool awarenessOnly)
    {
        var sut = BuildService();

        var result = await sut.FindToolDependencyImpactAsync(consumerId, includeAwarenessOnly: true);

        result.Should().Contain($"## Tool Dependency Impact - `{consumerId}`");
        result.Should().Contain("### Upstream Dependencies");
        result.Should().Contain($"`{producerId}`");
        result.Should().Contain($"Contract: {contractType}");
        result.Should().Contain(awarenessOnly ? "Awareness" : "Hard");
    }

    [Theory]
    [MemberData(nameof(CatalogEdges))]
    public async Task FindToolDependencyImpactAsync_AwarenessFiltering_MatchesCatalogEdgeImpact(
        string producerId,
        string consumerId,
        string _,
        bool awarenessOnly)
    {
        var sut = BuildService();

        var withoutAwareness = await sut.FindToolDependencyImpactAsync(producerId, includeAwarenessOnly: false);
        var withAwareness = await sut.FindToolDependencyImpactAsync(producerId, includeAwarenessOnly: true);

        if (awarenessOnly)
        {
            withoutAwareness.Should().NotContain($"`{consumerId}`");
            withAwareness.Should().Contain($"`{consumerId}`");
        }
        else
        {
            withoutAwareness.Should().Contain($"`{consumerId}`");
            withAwareness.Should().Contain($"`{consumerId}`");
        }
    }

    private static CodebaseQueryService BuildService() =>
        new(Substitute.For<ICodeGraphRepository>(), Substitute.For<IVectorRepository>());
}
