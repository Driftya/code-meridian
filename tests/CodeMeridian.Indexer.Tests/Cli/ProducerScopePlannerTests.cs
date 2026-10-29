using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class ProducerScopePlannerTests
{
    [Fact]
    public void NestedOverlappingRootsPartitionInputsOnceAndRetainLooseFiles()
    {
        var repository = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "scope-repository"));
        var selections = ProducerScopePlanner.Select(repository,
            [new(Path.Combine(repository.FullName, "web")), new(Path.Combine(repository.FullName, "web/nested"))],
            ["web/a.ts", "web/nested/b.ts", "web-other/c.ts", "loose.ts"]);

        selections.Single(scope => scope.Scope == "web").Paths.Should().Equal("web/a.ts");
        selections.Single(scope => scope.Scope == "web/nested").Paths.Should().Equal("web/nested/b.ts");
        selections.Single(scope => scope.Scope == ".").Paths.Should().Equal("loose.ts", "web-other/c.ts");
        selections.SelectMany(scope => scope.Paths).Should().OnlyHaveUniqueItems().And.HaveCount(4);
    }

    [Theory]
    [InlineData("../escape.ts")]
    [InlineData("src/./a.ts")]
    public void NonCanonicalInputsCannotBecomeMembershipEvidence(string path)
    {
        var repository = new DirectoryInfo(Path.GetTempPath());
        FluentActions.Invoking(() => ProducerScopePlanner.Select(repository, [], [path])).Should().Throw<ArgumentException>();
    }
}
