using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class ProjectFileDeletionServiceTests
{
    [Fact]
    public void NormalizeRelativePaths_DeduplicatesAndNormalizesRootedPaths()
    {
        using var workspace = TestWorkspace.Create();

        var result = ProjectFileDeletionService.NormalizeRelativePaths(
            ["docs/guide.md", Path.Combine(workspace.Root.FullName, "docs", "guide.md"), @"docs\other.md"],
            workspace.Root);

        result.Should().BeEquivalentTo(["docs/guide.md", "docs/other.md"]);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(DirectoryInfo root) => Root = root;

        public DirectoryInfo Root { get; }

        public static TestWorkspace Create()
        {
            var root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"codemeridian-delete-service-{Guid.NewGuid():N}"));
            root.Create();
            return new TestWorkspace(root);
        }

        public void Dispose()
        {
            if (Root.Exists)
                Root.Delete(recursive: true);
        }
    }
}
