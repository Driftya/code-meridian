using System.Text.Json;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.Indexer.Cli.SessionEvaluation;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class PrecisionFeedbackRoundTripTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-precision-feedback-roundtrip-tests",
        Guid.NewGuid().ToString("N"));

    public PrecisionFeedbackRoundTripTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task EvaluateAsync_PrecisionFeedbackRoundTripsIntoImplementationAndFeatureConsumers()
    {
        var sessionFile = WriteSession(
            """
            {"project":"CodeMeridian","kind":"graph-call","toolName":"mcp__CodeMeridian.find_implementation_surface","files":["src/Application/PaymentsAccepted.cs","src/Application/PaymentsIgnored.cs"],"targetConfidence":"exact,file-only"}
            {"project":"CodeMeridian","kind":"graph-call","toolName":"mcp__CodeMeridian.analyze_feature_implementation_path","files":["src/Application/FeatureAlphaAccepted.cs","src/Application/FeatureAlphaIgnored.cs"],"targetConfidence":"exact,file-only"}
            """);

        var evaluator = new SessionUsefulnessEvaluator(
            new SessionEvidenceReader(),
            new FakeChangeSource([
                "src/Application/PaymentsAccepted.cs",
                "src/Application/FeatureAlphaAccepted.cs"
            ]));

        var evaluation = await evaluator.EvaluateAsync(new SessionEvaluationOptions(
            new DirectoryInfo(_root),
            "CodeMeridian",
            sessionFile,
            "HEAD"));

        var feedbackPath = Path.Combine(_root, ".meridian", "precision-feedback.json");
        WritePrecisionFeedback(feedbackPath, evaluation.PrecisionFeedback);

        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var query = ci.Arg<CodeGraphQuery>();
                if (string.Equals(query.SemanticQuery, "update payments implementation", StringComparison.OrdinalIgnoreCase))
                {
                    return
                    [
                        Node("payments-accepted", "PaymentsAccepted", CodeNodeType.Class, "src/Application/PaymentsAccepted.cs", "CodeMeridian", "Payments implementation update"),
                        Node("payments-ignored", "PaymentsIgnored", CodeNodeType.Class, "src/Application/PaymentsIgnored.cs", "CodeMeridian", "Payments implementation update")
                    ];
                }

                return
                [
                    Node("feature-accepted", "FeatureAlphaAccepted", CodeNodeType.Class, "src/Application/FeatureAlphaAccepted.cs", "CodeMeridian", "FeatureAlpha implementation"),
                    Node("feature-ignored", "FeatureAlphaIgnored", CodeNodeType.Class, "src/Application/FeatureAlphaIgnored.cs", "CodeMeridian", "FeatureAlpha implementation")
                ];
            });
        graph.FindRelatedTestsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([]);
        vector.SearchByTextAsync("FeatureAlpha", "CodeMeridian", 6, Arg.Any<CancellationToken>())
            .Returns([]);

        var sut = new CodebaseQueryService(
            graph,
            vector,
            Options.Create(new CodebaseAnalysisOptions
            {
                PrecisionFeedback = new PrecisionFeedbackOptions
                {
                    FeedbackFilePath = feedbackPath
                }
            }));

        var implementationSurface = await sut.FindImplementationSurfaceAsync(
            "update payments implementation",
            projectContext: "CodeMeridian");
        var featurePath = await sut.AnalyzeFeatureImplementationPathAsync(
            "FeatureAlpha",
            "CodeMeridian");

        evaluation.PrecisionFeedback.Tools.Should().HaveCount(2);
        evaluation.PrecisionFeedback.Tools.Should().Contain(tool =>
            tool.ToolName == "mcp__CodeMeridian.find_implementation_surface"
            && tool.AcceptedFileCount == 1
            && tool.IgnoredFileCount == 1);
        evaluation.PrecisionFeedback.Tools.Should().Contain(tool =>
            tool.ToolName == "mcp__CodeMeridian.analyze_feature_implementation_path"
            && tool.AcceptedFileCount == 1
            && tool.IgnoredFileCount == 1);

        implementationSurface.IndexOf("src/Application/PaymentsAccepted.cs", StringComparison.Ordinal)
            .Should()
            .BeLessThan(implementationSurface.IndexOf("src/Application/PaymentsIgnored.cs", StringComparison.Ordinal));
        implementationSurface.Should().Contain("feedback accepted 1/1 prior sessions");
        implementationSurface.Should().Contain("feedback ignored 1/1 prior sessions");

        featurePath.IndexOf("src/Application/FeatureAlphaAccepted.cs", StringComparison.Ordinal)
            .Should()
            .BeLessThan(featurePath.IndexOf("src/Application/FeatureAlphaIgnored.cs", StringComparison.Ordinal));
        featurePath.Should().Contain("feedback accepted 1/1 prior sessions");
        featurePath.Should().Contain("feedback ignored 1/1 prior sessions");
    }

    [Fact]
    public async Task EvaluateAsync_PrecisionFeedbackPreservesDerivedAcceptanceSeparately()
    {
        var sessionFile = WriteSession(
            """
            {"project":"CodeMeridian","kind":"graph-call","toolName":"mcp__CodeMeridian.find_implementation_surface","files":["src/Application/PaymentsService.cs"],"targetConfidence":"exact"}
            {"project":"CodeMeridian","kind":"suggestion","toolName":"mcp__CodeMeridian.find_implementation_surface","files":["src/Application/Payments/PaymentsExtractor.cs"],"derivedFromFiles":["src/Application/PaymentsService.cs"],"changeKind":"extract"}
            """);

        var evaluator = new SessionUsefulnessEvaluator(
            new SessionEvidenceReader(),
            new FakeChangeSource([
                "src/Application/Payments/PaymentsExtractor.cs"
            ]));

        var evaluation = await evaluator.EvaluateAsync(new SessionEvaluationOptions(
            new DirectoryInfo(_root),
            "CodeMeridian",
            sessionFile,
            "HEAD"));

        var feedbackPath = Path.Combine(_root, ".meridian", "precision-feedback.json");
        WritePrecisionFeedback(feedbackPath, evaluation.PrecisionFeedback);

        using var document = JsonDocument.Parse(File.ReadAllText(feedbackPath));
        var tool = document.RootElement.GetProperty("tools").EnumerateArray().Single();
        var file = tool.GetProperty("files").EnumerateArray().Single();

        evaluation.PrecisionFeedback.Tools.Should().ContainSingle(tool =>
            tool.ToolName == "mcp__CodeMeridian.find_implementation_surface"
            && tool.AcceptedFileCount == 0
            && tool.DerivedAcceptedFileCount == 1
            && tool.IgnoredFileCount == 0);
        tool.GetProperty("derivedAcceptedFileCount").GetInt32().Should().Be(1);
        file.GetProperty("derivedAcceptedCount").GetInt32().Should().Be(1);
        file.GetProperty("derivedPaths").EnumerateArray().Select(element => element.GetString())
            .Should()
            .BeEquivalentTo(["src/Application/Payments/PaymentsExtractor.cs"]);
    }

    private FileInfo WriteSession(string content)
    {
        var sessionDirectory = Directory.CreateDirectory(Path.Combine(_root, ".meridian", "sessions"));
        var sessionPath = Path.Combine(sessionDirectory.FullName, "session.jsonl");
        File.WriteAllText(sessionPath, content);
        return new FileInfo(sessionPath);
    }

    private static void WritePrecisionFeedback(string feedbackPath, SessionPrecisionFeedback feedback)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(feedbackPath)!);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        File.WriteAllText(feedbackPath, JsonSerializer.Serialize(feedback, options));
    }

    private static CodeNode Node(
        string id,
        string name,
        CodeNodeType type,
        string filePath,
        string projectContext,
        string summary) => new()
    {
        Id = id,
        Name = name,
        Type = type,
        FilePath = filePath,
        ProjectContext = projectContext,
        Summary = summary,
        UpdatedAt = DateTimeOffset.UtcNow,
        LineNumber = 1,
        LineCount = 20
    };

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
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
    }
}
