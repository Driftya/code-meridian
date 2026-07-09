using System.Net;
using System.Net.Http.Json;
using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

[Collection(EnvironmentVariableTestCollection.Name)]
public sealed class StatusCommandTests
{
    [Fact]
    public async Task RunDoctorAsync_WhenServerResponds_PrintsStatusAndReturnsZeroWhenNeo4jIsReachable()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);
        using var listener = new HttpListener();
        var prefix = $"http://127.0.0.1:{GetFreePort()}/";
        listener.Prefixes.Add(prefix);
        listener.Start();
        var responseTask = RespondOnceAsync(listener, JsonContent.Create(new
        {
            ProjectContext = "CodeMeridian",
            Neo4jReachable = true,
            IndexedNodes = 123,
            CallEdges = 45,
            DocumentsIndexed = 6,
            DiagnosticsIndexed = 7,
            GraphDrift = "low",
            GraphDriftReport = "all good",
            EmbeddingsEnabled = true,
            EmbeddingProvider = "ollama",
            EmbeddingDimensions = 768,
            Error = "watch stale"
        }));

        try
        {
            var sut = new StatusCommand();

            var exitCode = await sut.RunDoctorAsync("CodeMeridian", prefix.TrimEnd('/'), null);
            await responseTask;

            exitCode.Should().Be(0);
            output.ToString().Should().Contain("CodeMeridian doctor");
            output.ToString().Should().Contain("Neo4j reachable         : yes");
            output.ToString().Should().Contain("Graph drift             : low");
            output.ToString().Should().Contain("Embeddings              : ollama (768 dims)");
            output.ToString().Should().Contain("Note                    : watch stale");
        }
        finally
        {
            Console.SetOut(originalOut);
            output.Dispose();
        }
    }

    [Fact]
    public async Task RunDriftVerificationAsync_WhenThresholdIsMet_ReturnsTwoAndPrintsReport()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);
        using var listener = new HttpListener();
        var prefix = $"http://127.0.0.1:{GetFreePort()}/";
        listener.Prefixes.Add(prefix);
        listener.Start();
        var responseTask = RespondOnceAsync(listener, JsonContent.Create(new
        {
            ProjectContext = "CodeMeridian",
            Neo4jReachable = true,
            IndexedNodes = 123,
            CallEdges = 45,
            DocumentsIndexed = 6,
            DiagnosticsIndexed = 7,
            GraphDrift = "high",
            GraphDriftReport = "drift report body",
            EmbeddingsEnabled = false,
            EmbeddingProvider = "",
            EmbeddingDimensions = 0,
            Error = (string?)null
        }));

        try
        {
            var sut = new StatusCommand();

            var exitCode = await sut.RunDriftVerificationAsync("CodeMeridian", prefix.TrimEnd('/'), null, "moderate");
            await responseTask;

            exitCode.Should().Be(2);
            output.ToString().Should().Contain("CodeMeridian drift verification");
            output.ToString().Should().Contain("Fail threshold         : moderate");
            output.ToString().Should().Contain("Graph drift            : high");
            output.ToString().Should().Contain("drift report body");
        }
        finally
        {
            Console.SetOut(originalOut);
            output.Dispose();
        }
    }

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

    [Fact]
    public async Task RunTraceEndpointAsync_WhenServerResponds_PrintsTrace()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);

        try
        {
            var sut = new StatusCommand((_, _) => new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Trace for /api/orders")
            }))
            {
                BaseAddress = new Uri("http://localhost")
            });

            var exitCode = await sut.RunTraceEndpointAsync("/api/orders", "CodeMeridian", "http://localhost", null, "Full");

            exitCode.Should().Be(0);
            output.ToString().Should().Contain("Trace for /api/orders");
        }
        finally
        {
            Console.SetOut(originalOut);
            output.Dispose();
        }
    }

    [Fact]
    public async Task RunTraceEndpointAsync_WhenServerReturnsEmpty_PrintsUnreachableError()
    {
        var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);

        try
        {
            var sut = new StatusCommand((_, _) => new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty)
            }))
            {
                BaseAddress = new Uri("http://localhost")
            });

            var exitCode = await sut.RunTraceEndpointAsync("/api/orders", "CodeMeridian", "http://localhost", null, "Compact");

            exitCode.Should().Be(1);
            error.ToString().Should().Contain("CodeMeridian trace-endpoint");
            error.ToString().Should().Contain("backend returned an empty or non-success response.");
        }
        finally
        {
            Console.SetError(originalError);
            error.Dispose();
        }
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private static async Task RespondOnceAsync(HttpListener listener, HttpContent content)
    {
        var context = await listener.GetContextAsync();
        context.Response.StatusCode = 200;
        context.Response.ContentType = content.Headers.ContentType?.ToString() ?? "application/json";
        await using var responseStream = await content.ReadAsStreamAsync();
        await responseStream.CopyToAsync(context.Response.OutputStream);
        context.Response.Close();
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
