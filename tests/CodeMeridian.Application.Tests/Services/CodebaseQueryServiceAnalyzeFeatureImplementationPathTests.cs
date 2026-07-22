using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceAnalyzeFeatureImplementationPathTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task AnalyzeFeatureImplementationPathAsync_WithDocsCodeAndTests_ReturnsEvidenceAndRisk()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var sut = new CodebaseQueryService(graph, vector);
        var service = Node(
            "s1",
            "FeatureImplementationAnalysisService",
            CodeNodeType.Class,
            "src/Application/Services/FeatureImplementationAnalysisService.cs",
            12,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 60,
            sourceHash: "abc",
            summary: "Maps feature docs to likely implementation surfaces.");
        var tool = Node(
            "m1",
            "AnalyzeFeatureImplementationPathAsync",
            CodeNodeType.Method,
            "src/McpServer/Tools/CodebaseTools.cs",
            120,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 25,
            sourceHash: "def",
            summary: "MCP tool exposure for feature implementation path analysis.");
        var test = Node(
            "t1",
            "FeatureImplementationAnalysisServiceTests",
            CodeNodeType.Class,
            "tests/CodeMeridian.Application.Tests/Services/FeatureImplementationAnalysisServiceTests.cs",
            5,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 40,
            sourceHash: "ghi",
            fileRole: IndexedFileRole.Test);

        vector
            .SearchByTextAsync("Add Feature Implementation Path", "CodeMeridian", 6, Arg.Any<CancellationToken>())
            .Returns([
                new KnowledgeDocument
                {
                    Id = "feature-1",
                    Content = """
                              # Add Feature Implementation Path
                              - Status: pending
                              This feature maps feature docs to implementation surfaces and test seams.
                              """,
                    Source = "docs/features/39-add-feature-implementation-path.md",
                    ProjectContext = "CodeMeridian"
                }
            ]);
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([service, tool]);
        graph
            .FindRelatedTestsAsync(service.Id, "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([(test, "direct")]);
        graph
            .FindRelatedTestsAsync(tool.Id, "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.AnalyzeFeatureImplementationPathAsync(
            "Add Feature Implementation Path",
            "CodeMeridian");

        result.Should().Contain("## Feature Implementation Path");
        result.Should().Contain("documented_with_code_and_test_evidence");
        result.Should().Contain("Confidence:**");
        result.Should().Contain("FeatureImplementationAnalysisService");
        result.Should().Contain("Presentation/MCP");
        result.Should().Contain("Focused verification plan:");
        result.Should().Contain("Direct regression tests:");
        result.Should().Contain("FeatureImplementationAnalysisServiceTests");
        result.Should().Contain("docs/features/39-add-feature-implementation-path.md");
        result.Should().Contain("Risk level");
    }

    [Fact]
    public async Task AnalyzeFeatureImplementationPathAsync_CategorizesFeatureTestsAndSuggestsCommand()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var sut = new CodebaseQueryService(graph, vector, Options.Create(WithDotNetTestCommands()));
        var service = Node(
            "s1",
            "KeywordGraphJobService",
            CodeNodeType.Class,
            "src/Application/Services/KeywordGraphJobService.cs",
            12,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 60,
            sourceHash: "abc",
            summary: "Runs keyword graph rebuild jobs.");
        var tool = Node(
            "m1",
            "KeywordsStatusEndpoint",
            CodeNodeType.ApiEndpoint,
            "src/McpServer/Api/KeywordApiEndpoints.cs",
            40,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 20,
            sourceHash: "def",
            summary: "Returns keyword status details.");
        var directTest = Node(
            "t1",
            "KeywordGraphJobServiceTests",
            CodeNodeType.Class,
            "tests/Application/KeywordGraphJobServiceTests.cs",
            5,
            "CodeMeridian",
            fileRole: IndexedFileRole.Test);
        var apiTest = Node(
            "t2",
            "KeywordApiEndpointTests",
            CodeNodeType.Class,
            "tests/Api/KeywordApiEndpointTests.cs",
            8,
            "CodeMeridian",
            fileRole: IndexedFileRole.Test);

        vector
            .SearchByTextAsync("keyword graph jobs", "CodeMeridian", 6, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([service, tool]);
        graph
            .FindRelatedTestsAsync(service.Id, "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([(directTest, "direct")]);
        graph
            .FindRelatedTestsAsync(tool.Id, "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([(apiTest, "direct")]);

        var result = await sut.AnalyzeFeatureImplementationPathAsync(
            "keyword graph jobs",
            "CodeMeridian");

        result.Should().Contain("Focused verification plan:");
        result.Should().Contain("Direct regression tests:");
        result.Should().Contain("KeywordGraphJobServiceTests");
        result.Should().Contain("Contract/API forwarding tests:");
        result.Should().Contain("KeywordApiEndpointTests");
        result.Should().Contain("Suggested command:");
    }

    [Fact]
    public async Task AnalyzeFeatureImplementationPathAsync_UsesPrecisionFeedbackInSurfaceReasons()
    {
        var feedbackPath = WritePrecisionFeedbackFile(
            """
            {
              "project": "CodeMeridian",
              "sessionFile": ".meridian/sessions/session.jsonl",
              "generatedAtUtc": "2026-06-19T00:00:00Z",
              "tools": [
                {
                  "toolName": "mcp__CodeMeridian.analyze_feature_implementation_path",
                  "suggestedFileCount": 1,
                  "acceptedFileCount": 1,
                  "ignoredFileCount": 0,
                  "suggestedTestCount": 0,
                  "acceptedTestCount": 0,
                  "ignoredTestCount": 0,
                  "exactTargets": 1,
                  "fileOnlyTargets": 0,
                  "heuristicTargets": 0,
                  "staleTargets": 0,
                  "staleWarnings": 0,
                  "manualFallbackCommands": 0,
                  "files": [
                    { "path": "src/Application/Services/FeatureImplementationAnalysisService.cs", "suggestedCount": 1, "acceptedCount": 1, "ignoredCount": 0 }
                  ],
                  "tests": []
                }
              ]
            }
            """);
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
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
        var service = Node(
            "s1",
            "FeatureImplementationAnalysisService",
            CodeNodeType.Class,
            "src/Application/Services/FeatureImplementationAnalysisService.cs",
            12,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 60,
            sourceHash: "abc",
            summary: "Maps feature docs to implementation surfaces.");

        vector
            .SearchByTextAsync("Add Feature Implementation Path", "CodeMeridian", 6, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([service]);
        graph
            .FindRelatedTestsAsync(service.Id, "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.AnalyzeFeatureImplementationPathAsync(
            "Add Feature Implementation Path",
            "CodeMeridian");

        result.Should().Contain("feedback accepted 1/1 prior sessions");
    }

    [Fact]
    public async Task AnalyzeFeatureImplementationPathAsync_WhenNoEvidence_ReturnsMissingGraphEvidence()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var sut = new CodebaseQueryService(graph, vector);

        vector
            .SearchByTextAsync("Add Ghost Feature", "CodeMeridian", 6, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.AnalyzeFeatureImplementationPathAsync(
            "Add Ghost Feature",
            "CodeMeridian");

        result.Should().Contain("not_found_in_graph");
        result.Should().Contain("Confidence:** low");
        result.Should().Contain("No matching KnowledgeDocument");
        result.Should().Contain("No CodeNode implementation surface matched");
        result.Should().Contain("No related test nodes were linked");
        result.Should().Contain("Risk level:** unknown");
    }

    [Fact]
    public async Task AnalyzeFeatureImplementationPathAsync_PrefersFeatureDocTermsOverGenericDocumentWords()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var sut = new CodebaseQueryService(graph, vector);
        var featureNode = Node(
            "surface",
            "GetContextForEditingAsync",
            CodeNodeType.Method,
            "src/Application/Services/CodebaseQueryService.Surface.cs",
            12,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            sourceHash: "surface",
            summary: "Builds derived edit surface context for refactor workflows.");
        var noisyNode = Node(
            "repo",
            "ICodeGraphRepository",
            CodeNodeType.Interface,
            "src/Core/CodeGraph/ICodeGraphRepository.cs",
            5,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            sourceHash: "repo",
            summary: "Repository contract for graph queries.");

        vector
            .SearchByTextAsync("docs/features/56-add-derived-edit-surface-credit-for-extraction-refactors.md", "CodeMeridian", 6, Arg.Any<CancellationToken>())
            .Returns([
                new KnowledgeDocument
                {
                    Id = "feature-56",
                    Source = "docs/features/56-add-derived-edit-surface-credit-for-extraction-refactors.md",
                    ProjectContext = "CodeMeridian",
                    Content = """
                              # Add Derived Edit Surface Credit For Extraction Refactors
                              - Status: pending
                              Suggested files from a prior session should receive derived edit-surface credit.
                              """
                }
            ]);
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([noisyNode, featureNode]);
        graph
            .FindRelatedTestsAsync(Arg.Any<string>(), "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.AnalyzeFeatureImplementationPathAsync(
            "docs/features/56-add-derived-edit-surface-credit-for-extraction-refactors.md",
            "CodeMeridian");

        result.IndexOf("src/Application/Services/CodebaseQueryService.Surface.cs", StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf("src/Core/CodeGraph/ICodeGraphRepository.cs", StringComparison.Ordinal));
        result.Should().Contain("`derived`");
        result.Should().Contain("`surface`");
        result.Should().NotContain("`suggested`");
        result.Should().NotContain("`files`");
        result.Should().NotContain("`session`");
    }


}

