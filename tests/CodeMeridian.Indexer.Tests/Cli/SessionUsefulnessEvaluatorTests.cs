using CodeMeridian.Indexer.Cli.SessionEvaluation;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class SessionUsefulnessEvaluatorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-session-evaluator-tests",
        Guid.NewGuid().ToString("N"));

    public SessionUsefulnessEvaluatorTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task EvaluateAsync_WhenSuggestedFilesAndTestsMatchChanges_RatesHigh()
    {
        var sessionFile = WriteSession(
            """
            {"project":"App","kind":"graph-call","toolName":"mcp__CodeMeridian.find_implementation_surface","files":["src/App/OrderService.cs","src/App/OrderRepository.cs"],"tests":["tests/App.Tests/OrderServiceTests.cs"],"targetConfidence":"exact"}
            {"project":"App","kind":"test-run","command":"dotnet test tests/App.Tests","tests":["tests/App.Tests/OrderServiceTests.cs"]}
            """);

        var sut = CreateEvaluator([
            "src/App/OrderService.cs",
            "src/App/OrderRepository.cs"
        ]);

        var result = await sut.EvaluateAsync(new SessionEvaluationOptions(
            new DirectoryInfo(_root),
            "App",
            sessionFile,
            "HEAD"));

        result.Rating.Should().Be("high");
        result.SuggestedFilesEdited.Should().Be(2);
        result.SuggestedFiles.Count.Should().Be(2);
        result.SuggestedTestsChangedOrRun.Should().Be(1);
        result.ExactTargets.Should().Be(1);
        result.GraphCalls.Should().Be(1);
    }

    [Fact]
    public async Task EvaluateAsync_WhenSomeSuggestionsMatchAndFallbacksExist_RatesPartial()
    {
        var sessionFile = WriteSession(
            """
            {"project":"App","kind":"graph-call","toolName":"mcp__CodeMeridian.find_implementation_surface","files":["src/App/OrderService.cs","src/App/OrderRepository.cs"],"targetConfidence":"file-only"}
            {"project":"App","kind":"command","command":"rg -n \"Order\" src tests"}
            {"project":"App","kind":"stale-warning"}
            """);

        var sut = CreateEvaluator(["src/App/OrderService.cs", "src/App/OtherFile.cs"]);

        var result = await sut.EvaluateAsync(new SessionEvaluationOptions(
            new DirectoryInfo(_root),
            "App",
            sessionFile,
            "HEAD"));

        result.Rating.Should().Be("partial");
        result.SuggestedFilesEdited.Should().Be(1);
        result.FileOnlyTargets.Should().Be(1);
        result.ManualFallbackCommands.Should().Be(1);
        result.StaleWarnings.Should().Be(1);
    }

    [Fact]
    public async Task EvaluateAsync_WhenNoGraphEvidenceExists_RatesUnknown()
    {
        var sessionFile = WriteSession(
            """
            {"project":"App","kind":"command","command":"rg -n \"Order\" src tests"}
            """);

        var sut = CreateEvaluator(["src/App/OrderService.cs"]);

        var result = await sut.EvaluateAsync(new SessionEvaluationOptions(
            new DirectoryInfo(_root),
            "App",
            sessionFile,
            "HEAD"));

        result.Rating.Should().Be("unknown");
        result.Notes.Should().Contain("No suggested files were recorded; agents should log CodeMeridian tool results or imported transcript facts.");
    }

    [Fact]
    public async Task EvaluateAsync_FiltersEventsByProjectButKeepsUnscopedEvents()
    {
        var sessionFile = WriteSession(
            """
            {"project":"Other","kind":"graph-call","toolName":"mcp__CodeMeridian.find_implementation_surface","files":["src/Other/File.cs"]}
            {"kind":"graph-call","toolName":"mcp__CodeMeridian.find_implementation_surface","files":["src/App/OrderService.cs"],"targetConfidence":"heuristic"}
            """);

        var sut = CreateEvaluator(["src/App/OrderService.cs"]);

        var result = await sut.EvaluateAsync(new SessionEvaluationOptions(
            new DirectoryInfo(_root),
            "App",
            sessionFile,
            "HEAD"));

        result.SuggestedFiles.Should().BeEquivalentTo(["src/App/OrderService.cs"]);
        result.HeuristicTargets.Should().Be(1);
    }

    [Fact]
    public async Task EvaluateAsync_CountsContextPackOutcomeStatuses()
    {
        var sessionFile = WriteSession(
            """
            {"project":"App","kind":"tool-result","toolName":"mcp__CodeMeridian.build_minimal_context","contextPackStatus":"full","files":["src/App/OrderService.cs"]}
            {"project":"App","kind":"tool-result","toolName":"mcp__CodeMeridian.build_minimal_context","contextPackStatus":"degraded","files":["src/App/OrderRepository.cs"]}
            {"project":"App","kind":"tool-result","toolName":"mcp__CodeMeridian.build_minimal_context","contextPackStatus":"failed"}
            """);

        var sut = CreateEvaluator([
            "src/App/OrderService.cs",
            "src/App/OrderRepository.cs"
        ]);

        var result = await sut.EvaluateAsync(new SessionEvaluationOptions(
            new DirectoryInfo(_root),
            "App",
            sessionFile,
            "HEAD"));

        result.ContextPackFullSuccesses.Should().Be(1);
        result.ContextPackDegradedSuccesses.Should().Be(1);
        result.ContextPackHardFailures.Should().Be(1);
    }

    private FileInfo WriteSession(string content)
    {
        var sessionDirectory = Directory.CreateDirectory(Path.Combine(_root, ".meridian", "sessions"));
        var sessionPath = Path.Combine(sessionDirectory.FullName, "session.jsonl");
        File.WriteAllText(sessionPath, content);
        return new FileInfo(sessionPath);
    }

    private static SessionUsefulnessEvaluator CreateEvaluator(IEnumerable<string> changedFiles) =>
        new(new SessionEvidenceReader(), new FakeChangeSource(changedFiles));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeChangeSource(IEnumerable<string> changedFiles) : ISessionChangeSource
    {
        public Task<SessionChangeSet> GetChangesAsync(DirectoryInfo root, string gitBase, CancellationToken cancellationToken) =>
            Task.FromResult(new SessionChangeSet(changedFiles
                .Select(SessionPathNormalizer.Normalize)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)));
    }
}
