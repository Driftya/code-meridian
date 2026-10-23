using CodeMeridian.Core.RepositoryConstraints;
using FluentAssertions;

namespace CodeMeridian.Core.Tests.RepositoryConstraints;

public sealed class MarkdownReferenceTargetTests
{
    [Theory]
    [InlineData("../images/a%20b.png#part%201", "local", "images/a b.png", "part 1")]
    [InlineData("/images/a.png", "local", "images/a.png", null)]
    [InlineData("#heading", "fragment_only", "docs/a.md", "heading")]
    [InlineData("", "fragment_only", "docs/a.md", null)]
    [InlineData("https://example.invalid/a", "external", null, null)]
    [InlineData("HTTP://example.invalid/a", "external", null, null)]
    [InlineData("//example.invalid/a", "external", null, null)]
    [InlineData("mailto:a@example.invalid", "external", null, null)]
    [InlineData("data:image/png;base64,foo", "unsupported", null, null)]
    [InlineData("file:///tmp/a", "unsupported", null, null)]
    [InlineData("javascript:foo()", "unsupported", null, null)]
    [InlineData("C:/a.md", "unsupported", null, null)]
    [InlineData("a%3Ab.md", "unsupported", null, null)]
    [InlineData("a%00b.md", "unsupported", null, null)]
    [InlineData("a%5Cb.md", "unsupported", null, null)]
    [InlineData("..\\a.md", "unsupported", null, null)]
    [InlineData("a.md?download=1", "unsupported", null, null)]
    [InlineData("a%GG.md", "unsupported", null, null)]
    [InlineData("a%FF.md", "unsupported", null, null)]
    [InlineData("a%C3.md", "unsupported", null, null)]
    [InlineData("%C3%A5.md", "local", "docs/å.md", null)]
    [InlineData("a%2520b.md", "local", "docs/a%20b.md", null)]
    [InlineData("a.md#bad%", "unsupported", null, "bad%")]
    [InlineData("../../outside.md", "outside_repository", null, null)]
    [InlineData("%2e%2e/%2e%2e/outside.md", "outside_repository", null, null)]
    [InlineData("%252e%252e/a.md", "local", "docs/%2e%2e/a.md", null)]
    [InlineData("/", "unsupported", null, null)]
    [InlineData(".", "unsupported", null, null)]
    [InlineData("..", "unsupported", null, null)]
    [InlineData("../images/", "unsupported", null, null)]
    [InlineData("a/..", "unsupported", null, null)]
    [InlineData("../../", "outside_repository", null, null)]
    public void TargetsHaveDeterministicRepositoryRelativeSemantics(string target, string kind, string? path, string? fragment)
    {
        MarkdownReferenceTarget.Normalize(target, "docs/a.md").Should().Be(new MarkdownReferenceTarget(kind, path, fragment));
    }

    [Fact]
    public void ACombinedPathBeyondTheSupportedBoundIsUnsupportedRatherThanOutsideTheRepository()
    {
        MarkdownReferenceTarget.Normalize(new string('a', 2048), "docs/a.md").Kind.Should().Be("unsupported");
    }
}
