using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceParseWindowTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Theory]
    [InlineData("24h", 24 * 60)]
    [InlineData("1h",   1 * 60)]
    [InlineData("7d",  7 * 24 * 60)]
    [InlineData("30m", 30)]
    [InlineData("garbage", 24 * 60)] // default fallback
    [InlineData("",     24 * 60)]    // empty → default
    public async Task ParseWindow_ConvertsToCorrectTimeSpan(string window, int expectedMinutes)
    {
        var (sut, graph) = Build();

        TimeSpan? captured = null;

        graph.FindRecentlyChangedAsync(
                Arg.Any<string?>(),
                Arg.Do<TimeSpan>(ts => captured = ts),
                Arg.Any<CancellationToken>())
             .Returns([]);

        await sut.FindRecentlyChangedAsync(window: window);

        captured.Should().NotBeNull();
        captured!.Value.TotalMinutes.Should().BeApproximately(expectedMinutes, precision: 0.1);
    }

    // ── FindLargeNodesAsync ───────────────────────────────────────────────────


}

