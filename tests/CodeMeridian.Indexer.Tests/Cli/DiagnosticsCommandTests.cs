using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class DiagnosticsCommandTests
{
    [Fact]
    public void BuildDotnetBuildArguments_UsesIsolatedOutputPaths()
    {
        var rootPath = new DirectoryInfo(Path.Combine("C:", "repo"));

        var arguments = DiagnosticsCommand.BuildDotnetBuildArguments(rootPath);

        arguments.Should().ContainInOrder("build", "--no-restore", "--nologo");
        arguments.Should().Contain(argument => argument.StartsWith("-p:BaseOutputPath=", StringComparison.Ordinal));
        arguments.Should().NotContain(argument =>
            string.Equals(argument, "-p:BaseOutputPath=" + Path.Combine(rootPath.FullName, "bin"), StringComparison.OrdinalIgnoreCase));
    }
}
