using System.Net;
using System.Net.Http.Json;
using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

[Collection(EnvironmentVariableTestCollection.Name)]
public sealed class StatusCommandTests
{
    [Fact]
    public async Task RunVersionAsync_WhenServerResponds_PrintsToolAndMcpVersions()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);

        try
        {
            var sut = new StatusCommand((_, _) => new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    Component = "CodeMeridian.McpServer",
                    ProductVersion = "9.8.7",
                    GraphContractVersion = 3,
                    CacheVersion = 3
                })
            }))
            {
                BaseAddress = new Uri("http://localhost")
            });

            var exitCode = await sut.RunVersionAsync("http://localhost", null);

            exitCode.Should().Be(0);
            output.ToString().Should().Contain("Client tool version");
            output.ToString().Should().Contain("MCP server version     : 9.8.7");
            output.ToString().Should().Contain("MCP graph contract     : 3");
            output.ToString().Should().Contain("MCP cache version      : 3");
        }
        finally
        {
            Console.SetOut(originalOut);
            output.Dispose();
        }
    }

    [Fact]
    public async Task RunVersionAsync_WhenServerFetchFails_PrintsFetchFailed()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);

        try
        {
            var sut = new StatusCommand((_, _) => throw new HttpRequestException("boom"));

            var exitCode = await sut.RunVersionAsync("http://localhost", null);

            exitCode.Should().Be(0);
            output.ToString().Should().Contain("Client tool version");
            output.ToString().Should().Contain("MCP server version     : fetch failed");
        }
        finally
        {
            Console.SetOut(originalOut);
            output.Dispose();
        }
    }

    [Fact]
    public async Task RunReportAsync_WhenServerResponds_PrintsReport()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);

        try
        {
            var sut = new StatusCommand((_, _) => new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("# Architecture Weather Report\n\n**Weather:** Clear")
            }))
            {
                BaseAddress = new Uri("http://localhost")
            });

            var exitCode = await sut.RunReportAsync("CodeMeridian", "http://localhost", null);

            exitCode.Should().Be(0);
            output.ToString().Should().Contain("# Architecture Weather Report");
            output.ToString().Should().Contain("**Weather:** Clear");
        }
        finally
        {
            Console.SetOut(originalOut);
            output.Dispose();
        }
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
