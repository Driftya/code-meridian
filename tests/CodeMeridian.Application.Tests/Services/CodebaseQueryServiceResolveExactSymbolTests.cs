using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceResolveExactSymbolTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task ResolveExactSymbolAsync_WithFileAndLineHints_ReturnsCanonicalNodeIds()
    {
        var (sut, graph) = Build();
        var target = Node(
            "Method:CodeMeridian.Application.Services.CodebaseQueryService.FindImplementationSurfaceAsync",
            "FindImplementationSurfaceAsync",
            CodeNodeType.Method,
            "src/Application/Services/CodebaseQueryService.Surface.cs",
            8,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 40);

        graph
            .QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q =>
                    q.NameFilter == "FindImplementationSurfaceAsync"
                    && q.FilePathFilter == "src/Application/Services/CodebaseQueryService.Surface.cs"
                    && q.ProjectContext == "CodeMeridian"),
                Arg.Any<CancellationToken>())
            .Returns([target]);

        var result = await sut.ResolveExactSymbolAsync(
            "FindImplementationSurfaceAsync",
            "src/Application/Services/CodebaseQueryService.Surface.cs",
            line: 10,
            projectContext: "CodeMeridian");

        result.Should().Contain("## Exact Symbol Resolution");
        result.Should().Contain("**Confidence summary:** 1 exact");
        result.Should().Contain("Method:CodeMeridian.Application.Services.CodebaseQueryService.FindImplementationSurfaceAsync");
        result.Should().Contain("name/id match");
        result.Should().Contain("file match");
        result.Should().Contain("near line hint");
    }

    [Fact]
    public async Task ResolveExactSymbolAsync_WithClassTarget_PrefersExactClassBeforeConstructors()
    {
        var (sut, graph) = Build();
        var classNode = Node(
            "CodeMeridian::Class::CodeMeridian.Application.Services.CodebaseQueryService",
            "CodebaseQueryService",
            CodeNodeType.Class,
            "src/Application/Services/CodebaseQueryService.ToolDependencyImpact.cs",
            5,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 160,
            sourceHash: "class-hash");
        var constructorOne = Node(
            "CodeMeridian::Method::CodeMeridian.Application.Services.CodebaseQueryService::CodebaseQueryService(ICodeGraphRepository,IVectorRepository)",
            "CodebaseQueryService(ICodeGraphRepository,IVectorRepository)",
            CodeNodeType.Method,
            "src/Application/Services/CodebaseQueryService.cs",
            22,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 12,
            sourceHash: "ctor-1");
        var constructorTwo = Node(
            "CodeMeridian::Method::CodeMeridian.Application.Services.CodebaseQueryService::CodebaseQueryService(ICodeGraphRepository,IVectorRepository,IEmbeddingProvider)",
            "CodebaseQueryService(ICodeGraphRepository,IVectorRepository,IEmbeddingProvider)",
            CodeNodeType.Method,
            "src/Application/Services/CodebaseQueryService.cs",
            49,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 12,
            sourceHash: "ctor-2");

        graph
            .QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q =>
                    q.NameFilter == "CodebaseQueryService"
                    && q.ProjectContext == "CodeMeridian"),
                Arg.Any<CancellationToken>())
            .Returns([constructorOne, constructorTwo, classNode]);

        var result = await sut.ResolveExactSymbolAsync(
            "CodebaseQueryService",
            projectContext: "CodeMeridian");

        result.Should().Contain("## Exact Symbol Resolution");
        result.Should().Contain(classNode.Id);
        result.IndexOf(classNode.Id, StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf(constructorOne.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveExactSymbolAsync_WhenBroadLookupMissesExactClass_UsesTypedFallbackQueries()
    {
        var (sut, graph) = Build();
        var classNode = Node(
            "CodeMeridian::Class::CodeMeridian.Application.Services.CodebaseQueryService",
            "CodebaseQueryService",
            CodeNodeType.Class,
            "src/Application/Services/CodebaseQueryService.Surface.cs",
            6,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 964,
            sourceHash: "class-hash");
        var constructorOne = Node(
            "CodeMeridian::Method::CodeMeridian.Application.Services.CodebaseQueryService::CodebaseQueryService(ICodeGraphRepository,IVectorRepository)",
            "CodebaseQueryService(ICodeGraphRepository,IVectorRepository)",
            CodeNodeType.Method,
            "src/Application/Services/CodebaseQueryService.cs",
            22,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 12,
            sourceHash: "ctor-1");
        var constructorTwo = Node(
            "CodeMeridian::Method::CodeMeridian.Application.Services.CodebaseQueryService::CodebaseQueryService(ICodeGraphRepository,IVectorRepository,IEmbeddingProvider)",
            "CodebaseQueryService(ICodeGraphRepository,IVectorRepository,IEmbeddingProvider)",
            CodeNodeType.Method,
            "src/Application/Services/CodebaseQueryService.cs",
            49,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 12,
            sourceHash: "ctor-2");

        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q =>
                    q.NameFilter == "CodebaseQueryService"
                    && q.ProjectContext == "CodeMeridian"
                    && q.TypeFilter == null),
                Arg.Any<CancellationToken>())
            .Returns([constructorOne, constructorTwo]);
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q =>
                    q.NameFilter == "CodebaseQueryService"
                    && q.ProjectContext == "CodeMeridian"
                    && q.TypeFilter == CodeNodeType.Class),
                Arg.Any<CancellationToken>())
            .Returns([classNode]);
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q =>
                    q.NameFilter == "CodebaseQueryService"
                    && q.ProjectContext == "CodeMeridian"
                    && q.TypeFilter == CodeNodeType.Interface),
                Arg.Any<CancellationToken>())
            .Returns([]);
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q =>
                    q.NameFilter == "CodebaseQueryService"
                    && q.ProjectContext == "CodeMeridian"
                    && q.TypeFilter == CodeNodeType.Method),
                Arg.Any<CancellationToken>())
            .Returns([constructorOne, constructorTwo]);
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q =>
                    q.NameFilter == "CodebaseQueryService"
                    && q.ProjectContext == "CodeMeridian"
                    && q.TypeFilter == CodeNodeType.File),
                Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.ResolveExactSymbolAsync(
            "CodebaseQueryService",
            projectContext: "CodeMeridian");

        result.Should().Contain("## Exact Symbol Resolution");
        result.Should().Contain("**Confidence summary:** 1 exact");
        result.Should().Contain(classNode.Id);
        result.IndexOf(classNode.Id, StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf(constructorOne.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveExactSymbolAsync_WithDuplicateMethodNamesAndFileHint_PrefersMatchingCanonicalNode()
    {
        var (sut, graph) = Build();
        var applicationNode = Node(
            "CodeMeridian::Method::CodeMeridian.Application.Services.CodebaseQueryService::BuildMinimalContextAsync(string,string?,int,bool,bool,bool,bool,ContextDetailLevel,CancellationToken)",
            "BuildMinimalContextAsync",
            CodeNodeType.Method,
            "src/Application/Services/CodebaseQueryService.Analytics.cs",
            938,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 140,
            sourceHash: "app-build-minimal-context");
        var mcpNode = Node(
            "CodeMeridian::Method::CodeMeridian.McpServer.Tools.CodebaseTools::BuildMinimalContextAsync(string,string?,int,bool,bool,bool,bool,ContextDetailLevel,CancellationToken)",
            "BuildMinimalContextAsync",
            CodeNodeType.Method,
            "src/McpServer/Tools/CodebaseTools.Analytics.cs",
            174,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 20,
            sourceHash: "mcp-build-minimal-context");

        graph
            .QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q =>
                    q.NameFilter == "BuildMinimalContextAsync"
                    && q.FilePathFilter == "src/Application/Services/CodebaseQueryService.Analytics.cs"
                    && q.ProjectContext == "CodeMeridian"),
                Arg.Any<CancellationToken>())
            .Returns([applicationNode, mcpNode]);

        var result = await sut.ResolveExactSymbolAsync(
            "BuildMinimalContextAsync",
            "src/Application/Services/CodebaseQueryService.Analytics.cs",
            line: 940,
            projectContext: "CodeMeridian");

        result.Should().Contain("## Exact Symbol Resolution");
        result.Should().Contain("**Confidence summary:** 1 exact");
        result.Should().Contain(applicationNode.Id);
        result.Should().NotContain(mcpNode.Id);
        result.Should().Contain("file match");
        result.Should().Contain("near line hint");
    }


}

