using CodeMeridian.RoslynIndexer.Pipeline;
using FluentAssertions;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class IndexerPipelineTests
{
    [Theory]
    [InlineData("src/App.cs", true)]
    [InlineData("src/App.CS", true)]
    [InlineData("docs/guide.md", false)]
    [InlineData("src/app.ts", false)]
    public void IsCSharpSourcePath_RecognizesOnlyCSharpFiles(string path, bool expected)
    {
        IndexerPipeline.IsCSharpSourcePath(path).Should().Be(expected);
    }
}
