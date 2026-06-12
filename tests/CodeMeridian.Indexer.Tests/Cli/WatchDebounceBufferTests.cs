using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class WatchDebounceBufferTests
{
    [Fact]
    public void RecordChange_DeduplicatesAndNormalizesPaths()
    {
        using var workspace = TestWorkspace.Create();
        var buffer = new WatchDebounceBuffer(workspace.Root);

        buffer.RecordChange(Path.Combine(workspace.Root.FullName, "docs", "guide.md"), deleted: false);
        buffer.RecordChange(Path.Combine(workspace.Root.FullName, "docs", "guide.md"), deleted: false);
        buffer.RecordChange(Path.Combine(workspace.Root.FullName, "docs", "gone.md"), deleted: true);
        buffer.RecordChange(Path.Combine(workspace.Root.FullName, "docs", "gone.md"), deleted: true);

        var batch = buffer.Drain();

        batch.ChangedFiles.Should().ContainSingle("docs/guide.md");
        batch.DeletedFiles.Should().ContainSingle("docs/gone.md");
    }

    [Fact]
    public void Drain_EmptiesPendingState()
    {
        using var workspace = TestWorkspace.Create();
        var buffer = new WatchDebounceBuffer(workspace.Root);

        buffer.RecordChange(Path.Combine(workspace.Root.FullName, "docs", "guide.md"), deleted: false);

        buffer.Drain();

        buffer.Drain().ChangedFiles.Should().BeEmpty();
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(DirectoryInfo root) => Root = root;

        public DirectoryInfo Root { get; }

        public static TestWorkspace Create()
        {
            var root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"codemeridian-watch-buffer-{Guid.NewGuid():N}"));
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
