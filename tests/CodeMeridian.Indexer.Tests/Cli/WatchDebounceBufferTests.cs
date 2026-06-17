using CodeMeridian.Tooling.Watching;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

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

    [Fact]
    public void RecordChange_WhenFileIsChangedThenDeleted_KeepsOnlyDelete()
    {
        using var workspace = TestWorkspace.Create();
        var buffer = new WatchDebounceBuffer(workspace.Root);
        var path = Path.Combine(workspace.Root.FullName, "src", "App.cs");

        buffer.RecordChange(path, deleted: false);
        buffer.RecordChange(path, deleted: true);

        var batch = buffer.Drain();

        batch.ChangedFiles.Should().BeEmpty();
        batch.DeletedFiles.Should().ContainSingle("src/App.cs");
    }

    [Fact]
    public async Task ScheduleChange_WhenChangeArrivesDuringProcessing_RunsFollowUpBatchWithoutOverlap()
    {
        using var workspace = TestWorkspace.Create();
        var scheduler = new WatchBatchScheduler(workspace.Root, NullLogger.Instance, TimeSpan.FromMilliseconds(10));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeRuns = 0;
        var maxActiveRuns = 0;
        var batches = new List<WatchDebounceBatch>();

        scheduler.ScheduleChange(
            Path.Combine(workspace.Root.FullName, "src", "First.cs"),
            deleted: false,
            OnBatchAsync,
            cts.Token);

        await firstStarted.Task.WaitAsync(cts.Token);

        scheduler.ScheduleChange(
            Path.Combine(workspace.Root.FullName, "src", "Second.cs"),
            deleted: false,
            OnBatchAsync,
            cts.Token);

        await Task.Delay(50, cts.Token);
        batches.Should().ContainSingle();

        releaseFirst.SetResult();
        await secondCompleted.Task.WaitAsync(cts.Token);

        batches.Should().HaveCount(2);
        batches[0].ChangedFiles.Should().ContainSingle("src/First.cs");
        batches[1].ChangedFiles.Should().ContainSingle("src/Second.cs");
        maxActiveRuns.Should().Be(1);

        async Task OnBatchAsync(WatchDebounceBatch batch, CancellationToken cancellationToken)
        {
            var running = Interlocked.Increment(ref activeRuns);
            maxActiveRuns = Math.Max(maxActiveRuns, running);

            try
            {
                lock (batches)
                {
                    batches.Add(batch);
                }

                if (batches.Count == 1)
                {
                    firstStarted.SetResult();
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }
                else
                {
                    secondCompleted.SetResult();
                }
            }
            finally
            {
                Interlocked.Decrement(ref activeRuns);
            }
        }
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
