using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindSmellPathsTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindSmellPathsAsync_WhenNoPaths_ReturnsCleanMessage()
    {
        var (sut, graph) = Build();
        graph.FindSmellPathsAsync(null, 4, Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.FindSmellPathsAsync();

        result.Should().Contain("No dependency smell paths found");
        result.Should().Contain("forbidden layer-to-layer paths");
    }

    [Fact]
    public async Task FindSmellPathsAsync_WithPaths_ReturnsPathTable()
    {
        var (sut, graph) = Build();
        var source = Node("source", "PricingRules", CodeNodeType.Class, "src/Core/PricingRules.cs");
        var middle = Node("middle", "SqlOrderRepository", CodeNodeType.Class, "src/Application/Orders/SqlOrderRepository.cs");
        var target = Node("target", "Neo4jOrderStore", CodeNodeType.Class, "src/Infrastructure/Orders/Neo4jOrderStore.cs");

        graph.FindSmellPathsAsync("Shop", 4, Arg.Any<CancellationToken>())
            .Returns([
                new DependencySmellPath(
                    "Core → Infrastructure",
                    source,
                    target,
                    2,
                    [
                        new GraphPathStep(source, "Uses", null),
                        new GraphPathStep(middle, "DependsOn", null),
                        new GraphPathStep(target, null, null)
                    ])
            ]);

        var result = await sut.FindSmellPathsAsync("Shop");

        result.Should().Contain("## Dependency Smell Paths");
        result.Should().Contain("**1** shortest forbidden dependency paths");
        result.Should().Contain("Core → Infrastructure");
        result.Should().Contain("PricingRules");
        result.Should().Contain("Neo4jOrderStore");
        result.Should().Contain("`PricingRules` -[Uses]- `SqlOrderRepository` -[DependsOn]- `Neo4jOrderStore`");
        result.Should().Contain("safe-first version");
    }


}

