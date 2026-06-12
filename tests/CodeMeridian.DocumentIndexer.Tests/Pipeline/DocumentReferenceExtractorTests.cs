using CodeMeridian.DocumentIndexer.Pipeline;
using FluentAssertions;

namespace CodeMeridian.DocumentIndexer.Tests.Pipeline;

public sealed class DocumentReferenceExtractorTests
{
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
