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
        result.PrecisionFeedback.Tools.Should().ContainSingle();
        result.PrecisionFeedback.Tools[0].AcceptedFileCount.Should().Be(2);
        result.PrecisionFeedback.Tools[0].DerivedAcceptedFileCount.Should().Be(0);
        result.PrecisionFeedback.Tools[0].IgnoredFileCount.Should().Be(0);
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
        result.PrecisionFeedback.Tools[0].AcceptedFileCount.Should().Be(1);
        result.PrecisionFeedback.Tools[0].DerivedAcceptedFileCount.Should().Be(0);
        result.PrecisionFeedback.Tools[0].IgnoredFileCount.Should().Be(1);
        result.PrecisionFeedback.Tools[0].FileOnlyTargets.Should().Be(1);
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

    [Fact]
    public async Task EvaluateAsync_WhenDerivedLineageTargetsNewCollaborator_CreditsSuggestedSourceSeparately()
    {
        var sessionFile = WriteSession(
            """
            {"project":"App","kind":"graph-call","toolName":"mcp__CodeMeridian.find_implementation_surface","files":["src/App/ChainLifecycleService.cs"],"targetConfidence":"exact"}
            {"project":"App","kind":"suggestion","toolName":"mcp__CodeMeridian.find_implementation_surface","files":["src/App/Chains/ChainHandoffService.cs"],"derivedFromFiles":["src/App/ChainLifecycleService.cs"],"changeKind":"extract"}
            """);

        var sut = CreateEvaluator([
            "src/App/Chains/ChainHandoffService.cs",
            "src/App/OtherFile.cs"
        ]);

        var result = await sut.EvaluateAsync(new SessionEvaluationOptions(
            new DirectoryInfo(_root),
            "App",
            sessionFile,
            "HEAD"));

        result.Rating.Should().Be("high");
        result.SuggestedFilesEdited.Should().Be(0);
        result.DerivedSuggestedFiles.Should().BeEquivalentTo(["src/App/ChainLifecycleService.cs"]);
        result.DerivedChangedFiles.Should().BeEquivalentTo(["src/App/Chains/ChainHandoffService.cs"]);
        result.UnrelatedChangedFiles.Should().BeEquivalentTo(["src/App/OtherFile.cs"]);
        result.PrecisionFeedback.Tools[0].AcceptedFileCount.Should().Be(0);
        result.PrecisionFeedback.Tools[0].DerivedAcceptedFileCount.Should().Be(1);
        result.PrecisionFeedback.Tools[0].IgnoredFileCount.Should().Be(0);
        var fileSignal = result.PrecisionFeedback.Tools[0].Files.Should().ContainSingle().Subject;
        fileSignal.Path.Should().Be("src/App/ChainLifecycleService.cs");
        fileSignal.DerivedAcceptedCount.Should().Be(1);
        fileSignal.DerivedPaths.Should().BeEquivalentTo(["src/App/Chains/ChainHandoffService.cs"]);
    }

    [Fact]
    public async Task EvaluateAsync_WhenPlannedNamespaceMatchesChangedFile_CreditsDerivedMatch()
    {
        var sessionFile = WriteSession(
            """
            {"project":"App","kind":"graph-call","toolName":"mcp__CodeMeridian.find_implementation_surface","files":["src/App/ChainLifecycleService.cs"],"targetConfidence":"exact"}
            {"project":"App","kind":"suggestion","toolName":"mcp__CodeMeridian.find_implementation_surface","files":["src/App/ChainLifecycleService.cs"],"plannedNamespaces":["App.Chains.Collaboration"],"changeKind":"extract"}
            """);

        WriteWorkspaceFile(
            "src/App/OtherFolder/ChainHandoffService.cs",
            """
            namespace App.Chains.Collaboration;

            internal sealed class ChainHandoffService;
            """);

        var sut = CreateEvaluator([
            "src/App/OtherFolder/ChainHandoffService.cs"
        ]);

        var result = await sut.EvaluateAsync(new SessionEvaluationOptions(
            new DirectoryInfo(_root),
            "App",
            sessionFile,
            "HEAD"));

        result.DerivedSuggestedFiles.Should().BeEquivalentTo(["src/App/ChainLifecycleService.cs"]);
        result.DerivedChangedFiles.Should().BeEquivalentTo(["src/App/OtherFolder/ChainHandoffService.cs"]);
        result.UnrelatedChangedFiles.Should().BeEmpty();
        result.PrecisionFeedback.Tools[0].DerivedAcceptedFileCount.Should().Be(1);
    }

    [Fact]
    public async Task EvaluateAsync_WhenRenamePreservesSuggestedPathLineage_CreditsDerivedMatch()
    {
        var sessionFile = WriteSession(
            """
            {"project":"App","kind":"graph-call","toolName":"mcp__CodeMeridian.find_implementation_surface","files":["src/App/Legacy/OrderService.cs"],"targetConfidence":"exact"}
            """);

        var sut = CreateEvaluator(
            ["src/App/Services/OrderService.cs"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["src/App/Services/OrderService.cs"] = "src/App/Legacy/OrderService.cs"
            });

        var result = await sut.EvaluateAsync(new SessionEvaluationOptions(
            new DirectoryInfo(_root),
            "App",
            sessionFile,
            "HEAD"));

        result.DerivedSuggestedFiles.Should().BeEquivalentTo(["src/App/Legacy/OrderService.cs"]);
        result.DerivedChangedFiles.Should().BeEquivalentTo(["src/App/Services/OrderService.cs"]);
        result.UnrelatedChangedFiles.Should().BeEmpty();
        result.PrecisionFeedback.Tools[0].DerivedAcceptedFileCount.Should().Be(1);
    }

    private FileInfo WriteSession(string content)
    {
        var sessionDirectory = Directory.CreateDirectory(Path.Combine(_root, ".meridian", "sessions"));
        var sessionPath = Path.Combine(sessionDirectory.FullName, "session.jsonl");
        File.WriteAllText(sessionPath, content);
        return new FileInfo(sessionPath);
    }

    private void WriteWorkspaceFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private static SessionUsefulnessEvaluator CreateEvaluator(
        IEnumerable<string> changedFiles,
        IReadOnlyDictionary<string, string>? renamedFromByPath = null) =>
        new(new SessionEvidenceReader(), new FakeChangeSource(changedFiles, renamedFromByPath));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeChangeSource(
        IEnumerable<string> changedFiles,
        IReadOnlyDictionary<string, string>? renamedFromByPath) : ISessionChangeSource
    {
        public Task<SessionChangeSet> GetChangesAsync(DirectoryInfo root, string gitBase, CancellationToken cancellationToken) =>
            Task.FromResult(new SessionChangeSet(changedFiles
                .Select(SessionPathNormalizer.Normalize)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
                (renamedFromByPath ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
                .ToDictionary(
                    entry => SessionPathNormalizer.Normalize(entry.Key),
                    entry => SessionPathNormalizer.Normalize(entry.Value),
                    StringComparer.OrdinalIgnoreCase)));
    }
}
