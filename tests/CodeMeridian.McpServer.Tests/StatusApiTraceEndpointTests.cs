using CodeMeridian.Application.Services;
using CodeMeridian.McpServer.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace CodeMeridian.McpServer.Tests;

public sealed class StatusApiTraceEndpointTests
{
    [Fact]
    public async Task TraceEndpoint_ReturnsMarkdownPayload()
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.TraceEndpointAsync("POST /api/orders", "Shop.Api", ContextDetailLevel.Full, Arg.Any<CancellationToken>())
            .Returns("## Endpoint Trace");

        var method = typeof(StatusApiEndpoints).GetMethod("TraceEndpoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull();

        var result = await (Task<IResult>)method!.Invoke(
            null,
            ["POST /api/orders", "Shop.Api", "Full", queryService, CancellationToken.None])!;

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .AddRouting()
            .BuildServiceProvider();

        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var payload = await reader.ReadToEndAsync();
        payload.Should().Be("## Endpoint Trace");
    }
}
