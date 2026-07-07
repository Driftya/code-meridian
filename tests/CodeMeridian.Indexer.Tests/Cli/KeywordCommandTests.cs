using System.Net;
using System.Text;
using System.Text.Json;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Tooling.Configuration;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

[Collection(EnvironmentVariableTestCollection.Name)]
public sealed class KeywordCommandTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"codemeridian-keyword-command-{Guid.NewGuid():N}"));

    [Fact]
    public async Task RunAsync_Classify_SendsClassificationRequestAndPrintsSubmission()
    {
        using var server = new LoopbackKeywordServer(
            LoopbackKeywordServer.Json(HttpStatusCode.Accepted, new
            {
                accepted = true,
                message = "Started",
                job = new
                {
                    jobId = "11111111-2222-3333-4444-555555555555",
                    operation = "classify",
                    projectContext = "CodeMeridian",
                    state = "Running",
                    startedAt = "2026-07-08T10:00:00Z",
                    expiresAt = "2026-07-08T10:30:00Z",
                    completedAt = (string?)null,
                    summary = (string?)null,
                    error = (string?)null
                }
            }));

        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);

        try
        {
            var sut = new KeywordCommand(new StubToolConfigurationService(_root, server.Url, "test-api-key"));

            var exitCode = await sut.RunAsync(new KeywordCommandOptions(
                _root.FullName,
                "CodeMeridian",
                server.Url,
                Wait: false,
                LeaseTtlSeconds: 900,
                Action: KeywordCommandAction.Classify));

            exitCode.Should().Be(0);
            server.Requests.Should().ContainSingle();

            var request = server.Requests[0];
            request.Method.Should().Be("POST");
            request.Path.Should().Be("/api/v1/knowledge/keywords/classify");
            request.Authorization.Should().Be("Bearer test-api-key");

            using var body = JsonDocument.Parse(request.Body);
            body.RootElement.GetProperty("projectContext").GetString().Should().Be("CodeMeridian");
            body.RootElement.GetProperty("leaseTtlSeconds").GetInt32().Should().Be(900);

            output.ToString().Should().Contain("Starting keyword classification job");
            output.ToString().Should().Contain("Job started: classify 11111111-2222-3333-4444-555555555555 (Running)");
        }
        finally
        {
            Console.SetOut(originalOut);
            output.Dispose();
        }
    }

    [Fact]
    public async Task RunAsync_ClassifyWithWait_PollsUntilCompletion()
    {
        using var server = new LoopbackKeywordServer(
            LoopbackKeywordServer.Json(HttpStatusCode.Accepted, new
            {
                accepted = true,
                message = "Started",
                job = new
                {
                    jobId = "11111111-2222-3333-4444-555555555555",
                    operation = "classify",
                    projectContext = "CodeMeridian",
                    state = "Running",
                    startedAt = "2026-07-08T10:00:00Z",
                    expiresAt = "2026-07-08T10:30:00Z",
                    completedAt = (string?)null,
                    summary = (string?)null,
                    error = (string?)null
                }
            }),
            LoopbackKeywordServer.Json(HttpStatusCode.OK, new
            {
                jobId = "11111111-2222-3333-4444-555555555555",
                operation = "classify",
                projectContext = "CodeMeridian",
                state = "Completed",
                startedAt = "2026-07-08T10:00:00Z",
                expiresAt = "2026-07-08T10:30:00Z",
                completedAt = "2026-07-08T10:05:00Z",
                summary = "Classified 42 keywords.",
                error = (string?)null
            }));

        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);

        try
        {
            var sut = new KeywordCommand(new StubToolConfigurationService(_root, server.Url, null));

            var exitCode = await sut.RunAsync(new KeywordCommandOptions(
                _root.FullName,
                "CodeMeridian",
                server.Url,
                Wait: true,
                LeaseTtlSeconds: 900,
                Action: KeywordCommandAction.Classify));

            exitCode.Should().Be(0);
            server.Requests.Should().HaveCount(2);
            server.Requests[0].Path.Should().Be("/api/v1/knowledge/keywords/classify");
            server.Requests[1].Path.Should().Be("/api/v1/knowledge/keywords/jobs/11111111-2222-3333-4444-555555555555");
            output.ToString().Should().Contain("State    : Completed");
            output.ToString().Should().Contain("Classified 42 keywords.");
        }
        finally
        {
            Console.SetOut(originalOut);
            output.Dispose();
        }
    }

    [Fact]
    public async Task RunAsync_StatusWithoutJobId_ReturnsValidationError()
    {
        var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);

        try
        {
            var sut = new KeywordCommand(new StubToolConfigurationService(_root, "http://localhost", null));

            var exitCode = await sut.RunAsync(new KeywordCommandOptions(
                _root.FullName,
                "CodeMeridian",
                "http://localhost",
                Action: KeywordCommandAction.Status));

            exitCode.Should().Be(1);
            error.ToString().Should().Contain("--job-id is required for keyword status");
        }
        finally
        {
            Console.SetError(originalError);
            error.Dispose();
        }
    }

    public void Dispose()
    {
        if (_root.Exists)
            _root.Delete(recursive: true);
    }

    private sealed class StubToolConfigurationService(DirectoryInfo root, string url, string? apiKey) : IToolConfigurationService
    {
        public ToolConfigurationContext CreateContext(string? path) =>
            new(root, null, null, "CodeMeridian", url, apiKey);

        public string ResolveProject(ToolConfigurationContext context, string? overrideProject, bool includeFallback = true) =>
            overrideProject ?? context.EnvironmentProject ?? "CodeMeridian";

        public string ResolveCodeMeridianUrl(ToolConfigurationContext context, string? overrideUrl) =>
            overrideUrl ?? context.EnvironmentUrl ?? "http://localhost";

        public bool ResolveAllowRepoScripts(ToolConfigurationContext context, bool allowRepoScriptsOverride) => allowRepoScriptsOverride;

        public DirectoryInfo ResolveRootPath(string? path) => root;
    }

    private sealed class LoopbackKeywordServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Queue<Func<HttpListenerResponse, Task>> _responses;
        private readonly Task _serverTask;

        public LoopbackKeywordServer(params Func<HttpListenerResponse, Task>[] responses)
        {
            _responses = new Queue<Func<HttpListenerResponse, Task>>(responses);
            var prefix = $"http://127.0.0.1:{GetFreePort()}/";
            _listener.Prefixes.Add(prefix);
            _listener.Start();
            Url = prefix.TrimEnd('/');
            _serverTask = Task.Run(ServeAsync);
        }

        public string Url { get; }

        public List<RecordedRequest> Requests { get; } = [];

        public void Dispose()
        {
            _listener.Close();
            try
            {
                _serverTask.GetAwaiter().GetResult();
            }
            catch (HttpListenerException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public static Func<HttpListenerResponse, Task> Json(HttpStatusCode statusCode, object payload) =>
            async response =>
            {
                response.StatusCode = (int)statusCode;
                response.ContentType = "application/json";
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
                response.ContentLength64 = bytes.Length;
                await response.OutputStream.WriteAsync(bytes);
                response.OutputStream.Close();
            };

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                Requests.Add(new RecordedRequest(
                    context.Request.HttpMethod,
                    context.Request.Url!.AbsolutePath,
                    context.Request.Headers["Authorization"],
                    body));

                if (_responses.Count == 0)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    context.Response.OutputStream.Close();
                    continue;
                }

                var responder = _responses.Dequeue();
                await responder(context.Response);
            }
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

    private sealed record RecordedRequest(string Method, string Path, string? Authorization, string Body);
}
