using CodeMeridian.DocumentIndexer.Pipeline;
using FluentAssertions;

namespace CodeMeridian.DocumentIndexer.Tests.Pipeline;

public sealed class DocumentChunkReferenceBuilderTests
{
    [Fact]
    public void BuildChunkDocumentId_UsesPartSuffixForSplitDocuments()
    {
        DocumentChunkReferenceBuilder.BuildChunkDocumentId("Demo", "docs/large.md", 2, 1)
            .Should()
            .Be("Demo::doc::docs/large.md::part2");
    }

    [Fact]
    public void BuildAdjacentChunkIds_ReturnsPreviousAndNextChunkIds()
    {
        var result = DocumentChunkReferenceBuilder.BuildAdjacentChunkIds("Demo", "docs/large.md", 3, 1);

        result.Should().BeEquivalentTo([
            "Demo::doc::docs/large.md::part1",
            "Demo::doc::docs/large.md::part3"
        ]);
    }
}
