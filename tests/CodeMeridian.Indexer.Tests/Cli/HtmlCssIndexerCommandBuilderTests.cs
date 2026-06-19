using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class HtmlCssIndexerCommandBuilderTests
{
    [Fact]
    public void BuildHtmlCssIndexerArgs_ComposesExpectedCommand()
    {
        var indexerRoot = new DirectoryInfo(@"C:\repo\tools\HtmlCssIndexer");
        var root = new DirectoryInfo(@"C:\repo");

        var args = HtmlCssIndexerCommandBuilder.BuildHtmlCssIndexerArgs(indexerRoot, root, "CodeMeridian");

        args.Should().Equal(
            @"C:\repo\tools\HtmlCssIndexer\src\index.ts",
            @"C:\repo",
            "--project",
            "CodeMeridian");
    }

    [Fact]
    public void AddHtmlCssIndexerOptions_AddsExpectedFlags()
    {
        var args = new List<string>();
        var batchFile = new FileInfo(@"C:\temp\html-css-batch.json");

        HtmlCssIndexerCommandBuilder.AddHtmlCssIndexerOptions(
            args,
            "http://localhost:5100",
            batchFile);

        args.Should().ContainInOrder(
            "--url",
            "http://localhost:5100",
            "--batch-file",
            @"C:\temp\html-css-batch.json");
    }
}
