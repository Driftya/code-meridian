using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

/// <summary>
/// Shared builders for behavior-focused CodebaseQueryService analytics test fixtures.
/// </summary>
public abstract class CodebaseQueryServiceAnalyticsTestBase
{
    // ── Shared factory helpers ────────────────────────────────────────────────

    protected static (CodebaseQueryService Sut, ICodeGraphRepository Graph) Build()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        return (new CodebaseQueryService(graph, vector), graph);
    }

    protected static (CodebaseQueryService Sut, ICodeGraphRepository Graph) Build(CodebaseAnalysisOptions options)
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        return (new CodebaseQueryService(graph, vector, Options.Create(options)), graph);
    }

    protected static (CodebaseQueryService Sut, ICodeGraphRepository Graph, IProjectAnalysisOptionsResolver Resolver) Build(
        CodebaseAnalysisOptions defaultOptions,
        IProjectAnalysisOptionsResolver resolver)
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        return (
            new CodebaseQueryService(
                graph,
                vector,
                new NoOpEmbeddingProvider(),
                Options.Create(defaultOptions),
                Options.Create(new CodebaseIndexingOptions()),
                new DefaultAnalysisProfilePolicy(),
                resolver),
            graph,
            resolver);
    }

    protected static (CodebaseQueryService Sut, ICodeGraphRepository Graph, IEmbeddingProvider Embeddings) BuildWithEmbeddings()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var embeddings = Substitute.For<IEmbeddingProvider>();
        return (new CodebaseQueryService(graph, vector, embeddings), graph, embeddings);
    }

    protected static CodebaseAnalysisOptions WithDotNetTestCommands(CodebaseAnalysisOptions? options = null)
    {
        options ??= new CodebaseAnalysisOptions();
        options.TestCommands = new TestCommandOptions
        {
            Strategies =
            [
                new TestCommandStrategyOptions
                {
                    MatchFilePathContains = [".cs", "/tests/"],
                    BaseCommand = "dotnet test",
                    SingleTestTemplate = "--filter FullyQualifiedName~{value}",
                    SameDirectoryTemplate = "--filter FullyQualifiedName~{value}"
                }
            ]
        };

        return options;
    }

    protected static CodebaseAnalysisOptions WithMixedLanguageTestCommands(CodebaseAnalysisOptions? options = null)
    {
        options ??= new CodebaseAnalysisOptions();
        options.TestCommands = new TestCommandOptions
        {
            Strategies =
            [
                new TestCommandStrategyOptions
                {
                    MatchFilePathContains = [".cs", "/tests/"],
                    BaseCommand = "dotnet test",
                    SingleTestTemplate = "--filter FullyQualifiedName~{value}",
                    SameDirectoryTemplate = "--filter FullyQualifiedName~{value}"
                },
                new TestCommandStrategyOptions
                {
                    MatchFilePathContains = [".ts", ".tsx", ".spec.", ".test."],
                    BaseCommand = "vitest run",
                    SingleTestTemplate = "{value}",
                    SameDirectoryTemplate = "{value}"
                }
            ]
        };

        return options;
    }

    protected static CodebaseAnalysisOptions WithLegacyDotNetTestCommands(CodebaseAnalysisOptions? options = null)
    {
        options ??= new CodebaseAnalysisOptions();
        options.TestCommands = new TestCommandOptions
        {
            BaseCommand = "dotnet test",
            SingleTestTemplate = "--filter FullyQualifiedName~{value}",
            SameDirectoryTemplate = "--filter FullyQualifiedName~{value}"
        };

        return options;
    }

    protected static CodeNode Node(
        string id,
        string name,
        CodeNodeType type,
        string? file = null,
        int? line = null,
        string? project = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        int? lineCount = null,
        string? summary = null,
        string? sourceSnippet = null,
        string? sourceHash = null,
        IndexedFileRole fileRole = IndexedFileRole.Unknown,
        string? @namespace = null) => new()
    {
        Id = id,
        Name = name,
        Type = type,
        Namespace = @namespace,
        FilePath = file,
        LineNumber = line,
        LineCount = lineCount,
        Summary = summary,
        SourceSnippet = sourceSnippet,
        SourceHash = sourceHash,
        ProjectContext = project,
        CreatedAt = createdAt,
        UpdatedAt = updatedAt,
        FileRole = fileRole
    };

    protected static CodeNode CreateFrontendStyleDeclaration(
        string id,
        string selectorText,
        string filePath,
        int lineNumber,
        string propertyName,
        string rawValue,
        IDictionary<string, string>? extraProperties = null,
        string project = "Shop.Web")
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["externalKind"] = "CssDeclaration",
            ["selectorText"] = selectorText,
            ["propertyName"] = propertyName,
            ["rawValue"] = rawValue
        };

        if (extraProperties is not null)
        {
            foreach (var pair in extraProperties)
                properties[pair.Key] = pair.Value;
        }

        return new CodeNode
        {
            Id = id,
            Name = $"{propertyName}: {rawValue}",
            Type = CodeNodeType.ExternalConcept,
            FilePath = filePath,
            LineNumber = lineNumber,
            LineCount = 1,
            ProjectContext = project,
            Properties = properties
        };
    }

    protected static string WritePrecisionFeedbackFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"codemeridian-precision-feedback-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }

    protected static CodeNode NodeWithLineCount(
        string id,
        string name,
        CodeNodeType type,
        string? file = null,
        int? line = null,
        int? lineCount = null) => new()
    {
        Id = id,
        Name = name,
        Type = type,
        FilePath = file,
        LineNumber = line,
        LineCount = lineCount
    };

    // ── FindImpactAsync ───────────────────────────────────────────────────────


}

