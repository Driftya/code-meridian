using CodeMeridian.RoslynIndexer.Pipeline;
using FluentAssertions;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class IndexerPipelineTests
{
    [Fact]
    public void CSharpProjectScope_IncludesExternalTransitiveProjectReferences()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var external = Directory.CreateDirectory(Path.Combine(root.Parent!.FullName, "external-" + Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "App.csproj"), "<Project><ItemGroup><ProjectReference Include=\"../" + external.Name + "/Shared.csproj\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(root.FullName, "App.cs"), "class App {}");
            File.WriteAllText(Path.Combine(external.FullName, "Shared.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(external.FullName, "Shared.cs"), "class Shared {}");

            var files = CSharpProjectScope.EnumerateCSharpFiles(root);

            files.Select(file => file.Name).Should().Contain(["App.cs", "Shared.cs"]);
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
            Directory.Delete(external.FullName, recursive: true);
        }
    }

    [Fact]
    public void CSharpProjectScope_IncludesExternalProjectsListedBySlnx()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var external = Directory.CreateDirectory(Path.Combine(root.Parent!.FullName, "external-" + Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "App.slnx"), "<Solution><Project Path=\"../" + external.Name + "/Shared.csproj\" /></Solution>");
            File.WriteAllText(Path.Combine(external.FullName, "Shared.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(external.FullName, "Shared.cs"), "class Shared {}");

            var files = CSharpProjectScope.EnumerateCSharpFiles(root);

            files.Select(file => file.Name).Should().Contain("Shared.cs");
        }
        finally
        {
            Directory.Delete(root.FullName, recursive: true);
            Directory.Delete(external.FullName, recursive: true);
        }
    }

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
