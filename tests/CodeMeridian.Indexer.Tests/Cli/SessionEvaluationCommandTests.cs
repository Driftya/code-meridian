using CodeMeridian.Indexer.Cli.SessionEvaluation;
using CodeMeridian.Tooling.Configuration;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

 [Collection(EnvironmentVariableTestCollection.Name)]
public sealed class SessionEvaluationCommandTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"codemeridian-session-eval-{Guid.NewGuid():N}"));

    [Fact]
    public async Task RunAsync_PrintsReportAndWritesPrecisionFeedback()
    {
        var sessionFile = WriteSession("""
            {"project":"CodeMeridian","kind":"suggestion","toolName":"resolve_exact_symbol","files":["src/App.cs"],"tests":["tests/AppTests.cs"],"targetConfidence":"exact","contextPackStatus":"full"}
            """);
        var evaluator = new SessionUsefulnessEvaluator(new SessionEvidenceReader(), new StubSessionChangeSource(
            new SessionChangeSet(
                new HashSet<string>(["src/App.cs", "tests/AppTests.cs"], StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))));
        var sut = new SessionEvaluationCommand(new StubToolConfigurationService(_root), evaluator);
        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);

        try
        {
            var exitCode = await sut.RunAsync(_root.FullName, "CodeMeridian", sessionFile.FullName, "HEAD");

            exitCode.Should().Be(0);
        }
        finally
        {
            Console.SetOut(originalOut);
            output.Dispose();
        }

        var feedbackPath = Path.Combine(_root.FullName, ".meridian", "precision-feedback.json");
        File.Exists(feedbackPath).Should().BeTrue();
        output.ToString().Should().Contain("CodeMeridian usefulness:");
        output.ToString().Should().Contain("Suggested files edited directly: 1/1");
        output.ToString().Should().Contain("Precision feedback:");
    }

    [Fact]
    public async Task RunAsync_WhenEvaluationThrows_WritesError()
    {
        var evaluator = new SessionUsefulnessEvaluator(new SessionEvidenceReader(), new ThrowingSessionChangeSource());
        var sut = new SessionEvaluationCommand(new StubToolConfigurationService(_root), evaluator);
        var sessionFile = WriteSession("""{"project":"CodeMeridian","kind":"suggestion","files":["src/App.cs"]}""");
        var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);

        try
        {
            var exitCode = await sut.RunAsync(_root.FullName, "CodeMeridian", sessionFile.FullName, "HEAD");

            exitCode.Should().Be(1);
        }
        finally
        {
            Console.SetError(originalError);
            error.Dispose();
        }

        error.ToString().Should().Contain("error: git diff failed");
    }

    public void Dispose()
    {
        if (_root.Exists)
            _root.Delete(recursive: true);
    }

    private FileInfo WriteSession(string jsonl)
    {
        var file = new FileInfo(Path.Combine(_root.FullName, "session.jsonl"));
        File.WriteAllText(file.FullName, jsonl + Environment.NewLine);
        return file;
    }

    private sealed class StubToolConfigurationService(DirectoryInfo root) : IToolConfigurationService
    {
        public ToolConfigurationContext CreateContext(string? path) =>
            new(root, null, null, "CodeMeridian", "http://localhost", null);

        public string ResolveProject(ToolConfigurationContext context, string? overrideProject, bool includeFallback = true) =>
            overrideProject ?? context.EnvironmentProject ?? "CodeMeridian";

        public string ResolveCodeMeridianUrl(ToolConfigurationContext context, string? overrideUrl) =>
            overrideUrl ?? context.EnvironmentUrl ?? "http://localhost";

        public bool ResolveAllowRepoScripts(ToolConfigurationContext context, bool allowRepoScriptsOverride) => allowRepoScriptsOverride;

        public DirectoryInfo ResolveRootPath(string? path) => root;
    }

    private sealed class StubSessionChangeSource(SessionChangeSet changeSet) : ISessionChangeSource
    {
        public Task<SessionChangeSet> GetChangesAsync(DirectoryInfo root, string gitBase, CancellationToken cancellationToken) =>
            Task.FromResult(changeSet);
    }

    private sealed class ThrowingSessionChangeSource : ISessionChangeSource
    {
        public Task<SessionChangeSet> GetChangesAsync(DirectoryInfo root, string gitBase, CancellationToken cancellationToken) =>
            Task.FromException<SessionChangeSet>(new InvalidOperationException("git diff failed"));
    }
}
