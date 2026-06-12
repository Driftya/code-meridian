using System.Reflection;
using System.Text.Json;
using CodeMeridian.McpServer.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CodeMeridian.McpServer.Tests;

public sealed class StatusApiEndpointsTests
{
    [Fact]
    public async Task GetVersion_ReturnsAssemblyVersionMetadata()
    {
        var method = typeof(StatusApiEndpoints).GetMethod("GetVersion", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = (IResult)method!.Invoke(null, [])!;
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .AddRouting()
            .BuildServiceProvider();

        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Body.Position = 0;
        var payload = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);

        payload.GetProperty("component").GetString().Should().Be("CodeMeridian.McpServer");
        payload.GetProperty("productVersion").GetString().Should().NotBeNullOrWhiteSpace();
        payload.GetProperty("graphContractVersion").GetInt32().Should().BeGreaterThan(0);
        payload.GetProperty("cacheVersion").GetInt32().Should().BeGreaterThan(0);
    }
}
