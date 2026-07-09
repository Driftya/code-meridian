using CodeMeridian.Tooling.Watching;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class IndexWatchLoopTests
{
    [Fact]
    public async Task RunAsync_WhenCancelled_LogsStartAndStop()
    {
        using var workspace = TestWorkspace.Create();
        var logger = new RecordingLogger();
        var loop = new IndexWatchLoop(workspace.Root, logger, TimeSpan.FromMilliseconds(10));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var runTask = loop.RunAsync((_, _) => Task.CompletedTask, cts.Token);

        await WaitForAsync(() => logger.InformationMessages.Count > 0, cts.Token);
        cts.Cancel();
        await runTask;

        logger.InformationMessages.Should().Contain(message => message.Contains("Watch mode active", StringComparison.Ordinal));
        logger.InformationMessages.Should().Contain(message => message.Contains("Watch mode stopped.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_WhenFileIsCreated_EmitsChangedBatch()
    {
        using var workspace = TestWorkspace.Create();
        var logger = new RecordingLogger();
        var loop = new IndexWatchLoop(workspace.Root, logger, TimeSpan.FromMilliseconds(10));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var completion = new TaskCompletionSource<WatchDebounceBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
        var docsDirectory = Directory.CreateDirectory(Path.Combine(workspace.Root.FullName, "docs"));
        var guidePath = Path.Combine(docsDirectory.FullName, "guide.md");
        File.WriteAllText(guidePath, "# guide");

        var runTask = loop.RunAsync(
            (batch, _) =>
            {
                if (batch.ChangedFiles.Contains("docs/guide.md"))
                    completion.TrySetResult(batch);

                return Task.CompletedTask;
            },
            cts.Token);

        await WaitForAsync(() => logger.InformationMessages.Count > 0, cts.Token);

        File.AppendAllText(guidePath, Environment.NewLine + "updated");

        var batch = await completion.Task.WaitAsync(cts.Token);

        batch.ChangedFiles.Should().Contain("docs/guide.md");
        batch.DeletedFiles.Should().BeEmpty();

        cts.Cancel();
        await runTask;
    }

    [Fact]
    public async Task RunAsync_WhenIncludePathRejectsChange_DoesNotEmitBatch()
    {
        using var workspace = TestWorkspace.Create();
        var logger = new RecordingLogger();
        var loop = new IndexWatchLoop(
            workspace.Root,
            logger,
            TimeSpan.FromMilliseconds(10),
            fullPath => fullPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var invocationCount = 0;

        var runTask = loop.RunAsync(
            (_, _) =>
            {
                Interlocked.Increment(ref invocationCount);
                return Task.CompletedTask;
            },
            cts.Token);

        var srcDirectory = Directory.CreateDirectory(Path.Combine(workspace.Root.FullName, "src"));
        await WaitForAsync(() => logger.InformationMessages.Count > 0, cts.Token);

        File.WriteAllText(Path.Combine(srcDirectory.FullName, "App.cs"), "class App {}");

        await Task.Delay(250, cts.Token);

        invocationCount.Should().Be(0);

        cts.Cancel();
        await runTask;
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(25, cancellationToken);
        }
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(DirectoryInfo root) => Root = root;

        public DirectoryInfo Root { get; }

        public static TestWorkspace Create()
        {
            var root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"codemeridian-watch-loop-{Guid.NewGuid():N}"));
            root.Create();
            return new TestWorkspace(root);
        }

        public void Dispose()
        {
            if (Root.Exists)
                Root.Delete(recursive: true);
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> InformationMessages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Information)
                InformationMessages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
