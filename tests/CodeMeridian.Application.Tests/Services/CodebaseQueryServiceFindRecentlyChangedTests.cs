using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindRecentlyChangedTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindRecentlyChangedAsync_WhenEmpty_ReturnsNoChangesMessage()
    {
        var (sut, graph) = Build();
        graph.FindRecentlyChangedAsync(null, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindRecentlyChangedAsync(window: "7d");

        result.Should().Contain("No nodes created or updated in the last 7d");
        result.Should().Contain("timestamps are only tracked");
    }

    [Fact]
    public async Task FindRecentlyChangedAsync_WithResults_ReturnsAgeTable()
    {
        var (sut, graph) = Build();
        var changedAt = DateTimeOffset.UtcNow.AddHours(-2);

        graph.FindRecentlyChangedAsync("MyApi", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
             .Returns([(Node("n1", "NewHandler", CodeNodeType.Class, "src/Handler.cs"), changedAt, "created")]);

        var result = await sut.FindRecentlyChangedAsync("MyApi", "24h");

        result.Should().Contain("## Recently Changed — last 24h in MyApi");
        result.Should().Contain("**1** nodes changed");
        result.Should().Contain("NewHandler");
        result.Should().Contain("created");
        result.Should().Contain("h ago"); // "2h ago"
    }

    [Fact]
    public async Task FindRecentlyChangedAsync_VeryRecentNode_ShowsMinutesAgo()
    {
        var (sut, graph) = Build();
        var changedAt = DateTimeOffset.UtcNow.AddMinutes(-15);

        graph.FindRecentlyChangedAsync(null, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
             .Returns([(Node("n1", "Brand New", CodeNodeType.Method), changedAt, "updated")]);

        var result = await sut.FindRecentlyChangedAsync();

        result.Should().Contain("m ago"); // "15m ago"
    }

    [Fact]
    public async Task FindRecentlyChangedAsync_OlderThanADay_ShowsDaysAgo()
    {
        var (sut, graph) = Build();
        var changedAt = DateTimeOffset.UtcNow.AddDays(-3);

        graph.FindRecentlyChangedAsync(null, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
             .Returns([(Node("n1", "Old Change", CodeNodeType.Class), changedAt, "created")]);

        var result = await sut.FindRecentlyChangedAsync();

        result.Should().Contain("d ago"); // "3d ago"
    }

    // ── ParseWindow (via observable behaviour) ────────────────────────────────


}

