using System.Reflection;
using CodeMeridian.Indexer.Cli.SessionEvaluation;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class GitSessionChangeSourceTests
{
    [Fact]
    public void ParseChangeSet_TracksRenamesCopiesAndNormalizedPaths()
    {
        const string output = """
            M	src\Application\Services\OrderService.cs
            R100	src/OldName.cs	src/NewName.cs
            C100	src/Template.cs	src/Copy.cs
            X	src\Ignored\file.cs
            """;

        var method = typeof(GitSessionChangeSource).GetMethod("ParseChangeSet", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = (SessionChangeSet)method!.Invoke(null, [output])!;

        result.ChangedFiles.Should().BeEquivalentTo(["src/Application/Services/OrderService.cs", "src/NewName.cs", "src/Copy.cs", "src/Ignored/file.cs"]);
        result.RenamedFromByPath.Should().Contain(new KeyValuePair<string, string>("src/NewName.cs", "src/OldName.cs"));
        result.RenamedFromByPath.Should().Contain(new KeyValuePair<string, string>("src/Copy.cs", "src/Template.cs"));
    }
}
