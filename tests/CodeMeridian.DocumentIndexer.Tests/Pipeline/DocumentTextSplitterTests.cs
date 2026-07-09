using CodeMeridian.DocumentIndexer.Pipeline;
using FluentAssertions;

namespace CodeMeridian.DocumentIndexer.Tests.Pipeline;

public sealed class DocumentTextSplitterTests
{
    [Fact]
    public void SplitIntoChunks_PreservesParagraphBoundariesWhenPossible()
    {
        var text = """
            alpha

            beta

            gamma
            """;

        var chunks = DocumentTextSplitter.SplitIntoChunks(text, maxChars: 9);

        chunks.Should().Equal("alpha", "beta", "gamma");
    }

    [Fact]
    public void SplitIntoChunks_SlicesOversizedParagraphsAndFallsBackForWhitespace()
    {
        var oversized = new string('a', 7) + new string('b', 7);
        var chunks = DocumentTextSplitter.SplitIntoChunks(oversized, maxChars: 5);

        chunks.Should().Equal("aaaaa", "aabbb", "bbbb");

        DocumentTextSplitter.SplitIntoChunks("\r\n\r\n", maxChars: 2)
            .Should().Equal("\n\n");
    }
}
