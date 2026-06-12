using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class DocumentIndexRunCoordinatorTests
{
    [Fact]
    public void BuildPlan_WhenNoChangedFilesProvided_ReturnsAllDocumentationFilesWithoutDeletes()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteFile("docs/guide.md", "# guide");
        workspace.WriteFile("notes.txt", "notes");
        workspace.WriteFile("src/App.cs", "class App {}");

        var plan = DocumentIndexRunCoordinator.BuildPlan(workspace.Root, changedFiles: null, deletedFiles: []);

        plan.FilesToIngest.Select(file => Path.GetRelativePath(workspace.Root.FullName, file.FullName).Replace('\\', '/'))
            .Should()
            .BeEquivalentTo(["docs/guide.md", "notes.txt"]);
        plan.FilesToDelete.Should().BeEmpty();
    }

    [Fact]
    public void BuildPlan_WhenChangedFilesIncludeDocs_ReturnsMatchingDocsAndNormalizedDeletePaths()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteFile("docs/guide.md", "# guide");
        workspace.WriteFile("docs/other.md", "# other");

        var plan = DocumentIndexRunCoordinator.BuildPlan(
            workspace.Root,
            changedFiles: ["docs/guide.md", "src/App.cs"],
            deletedFiles: ["docs/other.md", Path.Combine(workspace.Root.FullName, "docs", "guide.md")]);

        plan.FilesToIngest.Select(file => Path.GetRelativePath(workspace.Root.FullName, file.FullName).Replace('\\', '/'))
            .Should()
            .ContainSingle("docs/guide.md");
        plan.FilesToDelete.Should().BeEquivalentTo(["docs/other.md", "docs/guide.md"]);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(DirectoryInfo root) => Root = root;

        public DirectoryInfo Root { get; }

        public static TestWorkspace Create()
        {
            var root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"codemeridian-doc-plan-{Guid.NewGuid():N}"));
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
