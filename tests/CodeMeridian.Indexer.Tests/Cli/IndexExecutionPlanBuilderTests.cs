using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class IndexExecutionPlanBuilderTests
{
    [Fact]
    public void EnumerateIndexableFiles_ExcludesIgnoredFoldersAndGeneratedFiles()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteFile("src/App.cs", "class App {}");
        workspace.WriteFile("bin/Generated.cs", "class Generated {}");
        workspace.WriteFile("obj/AssemblyInfo.cs", "class AssemblyInfo {}");
        workspace.WriteFile(".meridian/cache/state.json", "{}");
        workspace.WriteFile("src/Foo.generated.cs", "class GeneratedPartial {}");
        workspace.WriteFile("src/Bar.g.cs", "class GeneratedSuffix {}");

        var result = IndexExecutionPlanBuilder.EnumerateIndexableFiles(
            workspace.Root,
            includeCSharp: true,
            includeTypeScript: false,
            includeDocs: false,
            includeConfiguration: false);

        result.Select(file => Path.GetRelativePath(workspace.Root.FullName, file.FullName).Replace('\\', '/'))
            .Should()
            .Equal("src/App.cs");
    }

    [Fact]
    public void EnumerateIndexableFiles_IncludesDocumentationFilesAndCommonDocNames()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteFile("docs/guide.md", "# guide");
        workspace.WriteFile("README.md", "# readme");
        workspace.WriteFile("src/App.cs", "class App {}");

        var result = IndexExecutionPlanBuilder.EnumerateIndexableFiles(
            workspace.Root,
            includeCSharp: false,
            includeTypeScript: false,
            includeDocs: true,
            includeConfiguration: false);

        result.Select(file => Path.GetRelativePath(workspace.Root.FullName, file.FullName).Replace('\\', '/'))
            .Should()
            .BeEquivalentTo(["docs/guide.md", "README.md"]);
    }

    [Fact]
    public void IsTypeScriptSourceFile_ExcludesDeclarationFiles()
    {
        var file = new FileInfo(Path.Combine("C:", "repo", "types", "index.d.ts"));

        IndexExecutionPlanBuilder.IsTypeScriptSourceFile(file).Should().BeFalse();
    }

    [Fact]
    public void IsHtmlCssSourceFile_RecognizesFrontendMarkupAndStyles()
    {
        IndexExecutionPlanBuilder.IsHtmlCssSourceFile(new FileInfo(Path.Combine("C:", "repo", "src", "app.html"))).Should().BeTrue();
        IndexExecutionPlanBuilder.IsHtmlCssSourceFile(new FileInfo(Path.Combine("C:", "repo", "src", "site.css"))).Should().BeTrue();
        IndexExecutionPlanBuilder.IsHtmlCssSourceFile(new FileInfo(Path.Combine("C:", "repo", "src", "site.scss"))).Should().BeTrue();
        IndexExecutionPlanBuilder.IsHtmlCssSourceFile(new FileInfo(Path.Combine("C:", "repo", "src", "app.ts"))).Should().BeFalse();
    }

    [Fact]
    public void IsDocumentationFile_RecognizesCommonRepositoryDocs()
    {
        var file = new FileInfo(Path.Combine("C:", "repo", "README.md"));

        IndexExecutionPlanBuilder.IsDocumentationFile(file).Should().BeTrue();
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(DirectoryInfo root) => Root = root;

        public DirectoryInfo Root { get; }

        public static TestWorkspace Create()
        {
            var root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"codemeridian-index-plan-{Guid.NewGuid():N}"));
            root.Create();
            return new TestWorkspace(root);
        }

        public FileInfo WriteFile(string relativePath, string content)
        {
            var file = new FileInfo(Path.Combine(Root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            file.Directory!.Create();
            File.WriteAllText(file.FullName, content);
            return file;
        }

        public void Dispose()
        {
            if (Root.Exists)
                Root.Delete(recursive: true);
        }
    }
}
