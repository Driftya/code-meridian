using CodeMeridian.Core.Knowledge;
using FluentAssertions;

namespace CodeMeridian.Core.Tests;

public sealed class DocumentQueryScopeTests
{
    [Theory]
    [InlineData("docs/a.md", true)]
    [InlineData("docs/nested/a.md", true)]
    [InlineData("docs\\a.md", true)]
    [InlineData("docs-other/a.md", false)]
    [InlineData("Docs/a.md", false)]
    [InlineData("docs", false)]
    [InlineData("docs/../private.md", false)]
    [InlineData("/docs/a.md", false)]
    [InlineData("https://example.com/docs/a.md", false)]
    [InlineData(null, false)]
    public void RootsUseCanonicalOrdinalSegmentBoundaries(string? source, bool expected)
        => new DocumentQueryScope("docs").Contains(source).Should().Be(expected);

    [Fact]
    public void ExactTargetsPreserveSpacesAndCaseAndIncludeAllChunksOfTheSameSource()
    {
        var scope = new DocumentQueryScope("docs\\design notes", "docs\\design notes\\A.md");
        scope.RootPath.Should().Be("docs/design notes");
        scope.TargetPath.Should().Be("docs/design notes/A.md");
        scope.Contains("docs/design notes/A.md").Should().BeTrue();
        scope.Contains("docs/design notes/a.md").Should().BeFalse();
        scope.Contains("docs/design notes/A.md/child.md").Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("/docs")]
    [InlineData("../docs")]
    [InlineData("docs/../other")]
    [InlineData("docs//other")]
    [InlineData("docs/")]
    [InlineData("C:/docs")]
    [InlineData("docs\nother")]
    public void InvalidRootsAreRejected(string root)
    {
        var action = () => new DocumentQueryScope(root);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TargetsCannotEscapeTheRootOrNameTheRootItself()
    {
        var outside = () => new DocumentQueryScope("docs", "other/a.md");
        var same = () => new DocumentQueryScope("docs", "docs");
        outside.Should().Throw<ArgumentException>();
        same.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(".", true, true)]
    [InlineData("docs", true, true)]
    [InlineData("docs/design", true, true)]
    [InlineData("docs/design/nested", false, true)]
    [InlineData("docs/designer", false, false)]
    [InlineData("other", false, false)]
    public void EvidenceCoverageAndOverlapAreSeparate(string evidence, bool covers, bool overlaps)
    {
        var scope = new DocumentQueryScope("docs/design");
        scope.IsCoveredBy(evidence).Should().Be(covers);
        scope.Overlaps(evidence).Should().Be(overlaps);
    }
}
