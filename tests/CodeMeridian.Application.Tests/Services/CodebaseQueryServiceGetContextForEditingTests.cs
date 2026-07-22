using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceGetContextForEditingTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task GetContextForEditingAsync_WhenNodeNotFound_ReturnsNotFoundMessage()
    {
        var (sut, graph) = Build();
        graph.GetContextForEditingAsync("Unknown::Method", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(null, [], [], []));

        var result = await sut.GetContextForEditingAsync("Unknown::Method");

        result.Should().Contain("not found in the graph");
        result.Should().Contain("Unknown::Method");
        result.Should().Contain("query_codebase");
    }

    [Fact]
    public async Task GetContextForEditingAsync_WithCallers_ShowsCallerSection()
    {
        var (sut, graph) = Build();
        var target = NodeWithLineCount("t1", "SaveAsync", CodeNodeType.Method, "src/Repo.cs", 42, 25);
        var caller = Node("ca1", "OrderController.Create", CodeNodeType.Method, "src/Ctrl.cs", 10);

        graph.GetContextForEditingAsync("t1", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [caller], [], []));

        var result = await sut.GetContextForEditingAsync("t1");

        result.Should().Contain("## Edit Context — `SaveAsync`");
        result.Should().Contain("25 lines");
        result.Should().Contain("### Caller summary — showing 1 of 1 callers");
        result.Should().Contain("### Direct method callers (1)");
        result.Should().Contain("`OrderController.Create`");
        result.Should().Contain("src/Ctrl.cs");
        result.Should().Contain(":10");
        result.Should().Contain("direct production caller");
    }

    [Fact]
    public async Task GetContextForEditingAsync_WithNoCallers_DoesNotClaimSignatureChangeIsSafe()
    {
        var (sut, graph) = Build();
        var target = Node("t1", "InternalHelper", CodeNodeType.Method, "src/Helper.cs");

        graph.GetContextForEditingAsync("t1", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [], [], []));

        var result = await sut.GetContextForEditingAsync("t1");

        result.Should().Contain("Callers — none observed");
        result.Should().Contain("Empty relationship results are not proof that a change is safe");
        result.Should().NotContain("safe to change signature");
    }

    [Fact]
    public async Task GetContextForEditingAsync_WithCalleesAndInterfaces_IncludesBothSections()
    {
        var (sut, graph) = Build();
        var target = Node("t1", "ProcessOrder", CodeNodeType.Method, "src/Order.cs");
        var callee = Node("ce1", "SaveAsync", CodeNodeType.Method, "src/Repo.cs");
        var iface = Node("i1", "IOrderRepository", CodeNodeType.Interface);

        graph.GetContextForEditingAsync("t1", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [], [callee], [iface]));

        var result = await sut.GetContextForEditingAsync("t1");

        result.Should().Contain("### Calls (1)");
        result.Should().Contain("`SaveAsync`");
        result.Should().Contain("### Implements");
        result.Should().Contain("`IOrderRepository`");
        result.Should().Contain("find_impact");
    }

    [Fact]
    public async Task GetContextForEditingAsync_PrunesDuplicateFileOnlyCallers_AndGroupsCallerSections()
    {
        var (sut, graph) = Build();
        var target = Node("t1", "ChainService", CodeNodeType.Class, "src/Application/ChainService.cs", 12);
        var methodCaller = Node("m1", "ModerationWorkflow.Handle", CodeNodeType.Method, "src/Application/ModerationWorkflow.cs", 28);
        var classCaller = Node("c1", "ModerationWorkflow", CodeNodeType.Class, "src/Application/ModerationWorkflow.cs", 5);
        var duplicateFileCaller = Node("f1", "ModerationWorkflow.cs", CodeNodeType.File, "src/Application/ModerationWorkflow.cs", 1);
        var routeCaller = Node("r1", "POST /chains/{id}/moderate", CodeNodeType.ApiEndpoint, "src/Api/ChainsEndpoints.cs", 33);
        var testCaller = Node("t2", "ChainServiceTests", CodeNodeType.Class, "tests/Application/ChainServiceTests.cs", 7, fileRole: IndexedFileRole.Test);

        graph.GetContextForEditingAsync("t1", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [duplicateFileCaller, routeCaller, classCaller, testCaller, methodCaller], [], []));

        var result = await sut.GetContextForEditingAsync("t1");

        result.Should().Contain("### Direct method callers (1)");
        result.Should().Contain("ModerationWorkflow.Handle");
        result.Should().Contain("### Class/interface callers (1)");
        result.Should().Contain("`ModerationWorkflow`");
        result.Should().Contain("### Test callers (1)");
        result.Should().Contain("ChainServiceTests");
        result.Should().Contain("### Context-only file callers (1)");
        result.Should().Contain("POST /chains/{id}/moderate");
        result.Should().Contain("heuristic route metadata caller");
        result.Should().Contain("Suppressed 1 duplicate file-only callers");
        result.Should().NotContain("`ModerationWorkflow.cs` — `src/Application/ModerationWorkflow.cs`:1");
    }

    [Fact]
    public async Task GetContextForEditingAsync_WithFullDetail_IncludesRawCallerInventory()
    {
        var (sut, graph) = Build();
        var target = Node("t1", "ChainService", CodeNodeType.Class, "src/Application/ChainService.cs", 12);
        var methodCaller = Node("m1", "ModerationWorkflow.Handle", CodeNodeType.Method, "src/Application/ModerationWorkflow.cs", 28);
        var duplicateFileCaller = Node("f1", "ModerationWorkflow.cs", CodeNodeType.File, "src/Application/ModerationWorkflow.cs", 1);

        graph.GetContextForEditingAsync("t1", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [duplicateFileCaller, methodCaller], [], []));

        var result = await sut.GetContextForEditingAsync("t1", ContextDetailLevel.Full);

        result.Should().Contain("### Raw caller inventory (2)");
        result.Should().Contain("`ModerationWorkflow.cs`");
        result.Should().Contain("`ModerationWorkflow.Handle`");
    }

    // ── FindGodClassesAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetContextForEditingAsync_WithNamespaceStyleId_ResolvesCanonicalNodeId()
    {
        var (sut, graph) = Build();
        var target = Node(
            "CodeMeridian::Class::CodeMeridian.Tooling.Configuration.CodeMeridianConfigFileStore",
            "CodeMeridianConfigFileStore",
            CodeNodeType.Class,
            "src/Tooling/Configuration/CodeMeridianConfigFileStore.cs",
            7,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 503,
            sourceHash: "cfg-store");

        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q =>
                    q.NameFilter == "CodeMeridianConfigFileStore"
                    && q.ProjectContext == null),
                Arg.Any<CancellationToken>())
            .Returns([target]);
        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(target, [], [], []));

        var result = await sut.GetContextForEditingAsync("CodeMeridian.Tooling.Configuration.CodeMeridianConfigFileStore");

        result.Should().Contain("## Edit Context");
        result.Should().Contain("CodeMeridianConfigFileStore");
        await graph.Received(1).GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>());
        await graph.DidNotReceive().GetContextForEditingAsync("CodeMeridian.Tooling.Configuration.CodeMeridianConfigFileStore", Arg.Any<CancellationToken>());
    }


}

