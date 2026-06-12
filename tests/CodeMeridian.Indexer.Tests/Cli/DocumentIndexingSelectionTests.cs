using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class DocumentIndexingSelectionTests
{
    [Fact]
    public void FilterDocumentationRelativePaths_FiltersToDocumentationFilesOnly()
    {
        using var workspace = TestWorkspace.Create();

        var result = DocumentIndexingSelection.FilterDocumentationRelativePaths(
            ["docs/guide.md", "src/App.cs", "notes.txt", "frontend/app.tsx"],
            workspace.Root);

        result.Should().BeEquivalentTo(["docs/guide.md", "notes.txt"]);
    }

    [Fact]
    public void FilterDocumentationRelativePaths_NormalizesAbsolutePathsInsideWorkspace()
    {
        using var workspace = TestWorkspace.Create();
        var doc = workspace.WriteFile("docs/guide.md", "# guide");

        var result = DocumentIndexingSelection.FilterDocumentationRelativePaths(
            [doc.FullName],
            workspace.Root);

        result.Should().ContainSingle("docs/guide.md");
    }

    [Fact]
    public void SelectDocumentationFilesForIndexing_WhenChangedFilesProvided_ReturnsOnlyChangedDocs()
    {
        using var workspace = TestWorkspace.Create();
        var guide = workspace.WriteFile("docs/guide.md", "# guide");
        workspace.WriteFile("docs/other.md", "# other");

        var result = DocumentIndexingSelection.SelectDocumentationFilesForIndexing(
            [guide, new FileInfo(Path.Combine(workspace.Root.FullName, "docs", "other.md"))],
            workspace.Root,
            ["docs/guide.md", "src/App.cs"]);

        result.Select(file => Path.GetRelativePath(workspace.Root.FullName, file.FullName).Replace('\\', '/'))
            .Should()
            .Equal("docs/guide.md");
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(DirectoryInfo root) => Root = root;

        public DirectoryInfo Root { get; }

        public static TestWorkspace Create()
        {
            var root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"codemeridian-doc-select-{Guid.NewGuid():N}"));
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
