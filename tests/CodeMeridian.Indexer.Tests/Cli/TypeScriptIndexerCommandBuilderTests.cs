using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class TypeScriptIndexerCommandBuilderTests
{
    [Fact]
    public void BuildTypeScriptIndexerArgs_ComposesExpectedCommand()
    {
        var tsRoot = new DirectoryInfo(@"C:\repo\tools\TsIndexer");
        var root = new DirectoryInfo(@"C:\repo");

        var args = TypeScriptIndexerCommandBuilder.BuildTypeScriptIndexerArgs(tsRoot, root, "CodeMeridian");

        args.Should().Equal(
            @"C:\repo\tools\TsIndexer\src\index.ts",
            @"C:\repo",
            "--project",
            "CodeMeridian");
    }

    [Fact]
    public void AddTypeScriptIndexerOptions_AddsExpectedFlags()
    {
        var args = new List<string>();
        var filesList = new FileInfo(@"C:\temp\ts-files.txt");

        TypeScriptIndexerCommandBuilder.AddTypeScriptIndexerOptions(
            args,
            "http://localhost:5100",
            watch: true,
            clear: true,
            forceFull: true,
            includeDocs: false,
            filesList);

        args.Should().ContainInOrder(
            "--url",
            "http://localhost:5100",
            "--clear",
            "--full",
            "--no-docs",
            "--watch",
            "--files-list",
            @"C:\temp\ts-files.txt");
    }
}
