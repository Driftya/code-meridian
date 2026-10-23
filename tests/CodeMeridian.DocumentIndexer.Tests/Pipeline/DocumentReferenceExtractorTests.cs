using CodeMeridian.DocumentIndexer.Pipeline;
using FluentAssertions;

namespace CodeMeridian.DocumentIndexer.Tests.Pipeline;

public sealed class DocumentReferenceExtractorTests
{
    // Compatibility audit: searchable hints deliberately retain the legacy regex behavior.
    // These cases must not be used as structural validation evidence.
    [Theory]
    [InlineData("`[code](a.md)`", "docs/a.md")]
    [InlineData("\\[escaped](a.md)", "docs/a.md")]
    [InlineData("![image](a.png)", "docs/a.png")]
    [InlineData("[titled](a.md \"title\")", "docs/a.md \"title\"")]
    [InlineData("[encoded](a%20b.md)", "docs/a%20b.md")]
    [InlineData("[escape](../../outside.md)", "outside.md")]
    public void SearchHintsRetainKnownRegexLimits(string content, string expected)
    {
        DocumentReferenceExtractor.ExtractDocumentReferences(content, "docs/guide.md").Should().Equal(expected);
    }

    [Fact]
    public void SearchHintsDoNotExtractReferenceStyleLinks()
    {
        DocumentReferenceExtractor.ExtractDocumentReferences("[guide][ref]\n\n[ref]: a.md", "docs/guide.md").Should().BeEmpty();
    }

    [Fact]
    public void ExtractDocumentReferences_ResolvesNestedAndRootRelativeLinks()
    {
        var content = """
            [design](../docs/design.md)
            [root](/docs/features/01-add-build-minimal-context.md)
            [anchor](#local)
            [external](https://example.com/docs.md)
            """;

        var result = DocumentReferenceExtractor.ExtractDocumentReferences(content, "docs/guide.md");

        result.Should().BeEquivalentTo([
            "docs/design.md",
            "docs/features/01-add-build-minimal-context.md"
        ]);
    }
}
