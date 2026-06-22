using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceToolDependencyImpactTests
{
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
        result.Should().Contain("`tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs`");
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
    public async Task FindToolDependencyImpactAsync_WithUnknownSubject_ReturnsGuidance()
    {
        var sut = BuildService();

        var result = await sut.FindToolDependencyImpactAsync("not_a_real_tool");

        result.Should().Contain("No tracked tool dependency subject matched `not_a_real_tool`.");
        result.Should().Contain("`find_test_shield`");
        result.Should().Contain("`evaluate_session`");
    }

    private static CodebaseQueryService BuildService() =>
        new(Substitute.For<ICodeGraphRepository>(), Substitute.For<IVectorRepository>());
}
