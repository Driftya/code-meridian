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

        var args = TypeScriptIndexerCommandBuilder.BuildTypeScriptIndexerArgs(tsRoot, root, tsRoot, "CodeMeridian");

        args.Should().Equal(
            @"C:\repo\tools\TsIndexer\src\index.ts",
            @"C:\repo",
            "--workspace-root",
            @"C:\repo\tools\TsIndexer",
            "--project",
            "CodeMeridian");
    }

    [Fact]
    public void AddTypeScriptIndexerOptions_AddsExpectedFlags()
    {
        var args = new List<string>();
        var batchFile = new FileInfo(@"C:\temp\ts-batch.json");

        TypeScriptIndexerCommandBuilder.AddTypeScriptIndexerOptions(
            args,
            "http://localhost:5100",
            batchFile);

        args.Should().ContainInOrder(
            "--url",
            "http://localhost:5100",
            "--batch-file",
            @"C:\temp\ts-batch.json");
    }

    [Fact]
    public void AddTypeScriptIndexerOptions_ForIncrementalBatch_AddsPartialCatalogFlag()
    {
        var args = new List<string>();

        TypeScriptIndexerCommandBuilder.AddTypeScriptIndexerOptions(
            args,
            "http://localhost:5100",
            new FileInfo(@"C:\temp\ts-batch.json"),
            isIncremental: true);

        args.Should().Contain("--incremental");
    }
}
