using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Sdk;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class CSharpIndexerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-csharp-indexer-tests",
        Guid.NewGuid().ToString("N"));

    public CSharpIndexerTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task IndexAsync_ResolvesLocalCallEdgesToIndexedMethodIds()
    {
        var file = WriteFile(
            "src/Service.cs",
            """
            namespace Demo;

            public class Service
            {
                public void Caller()
                {
                    Callee();
                    System.Console.WriteLine("external");
                }

                public void Callee()
                {
                }
            }
            """);
        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = new CSharpIndexer(client, NullLogger<CSharpIndexer>.Instance);

        await sut.IndexAsync([file], "CodeMeridian", _root);

        var callEdges = handler.Requests
            .Where(request => request.Path == "/api/v1/knowledge/nodes/edges"
                              && request.Body.GetProperty("type").GetString() == "Calls")
            .ToArray();

        callEdges.Should().ContainSingle();
        callEdges[0].Body.GetProperty("sourceId").GetString().Should().Be("CodeMeridian::Method::Demo.Caller()");
        callEdges[0].Body.GetProperty("targetId").GetString().Should().Be("CodeMeridian::Method::Demo.Callee()");
    }

    [Fact]
    public async Task IndexAsync_WhenNamespaceAppearsInMultipleFiles_DoesNotThrow()
    {
        var caller = WriteFile(
            "src/Caller.cs",
            """
            namespace Demo;

            public class Caller
            {
                public void Run()
                {
                    Work();
                }

                public void Work()
                {
                }
            }
            """);
        var other = WriteFile(
            "src/Other.cs",
            """
            namespace Demo;

            public class Other
            {
                public void Ping()
                {
                }
            }
            """);
        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = new CSharpIndexer(client, NullLogger<CSharpIndexer>.Instance);

        var act = async () => await sut.IndexAsync([caller, other], "CodeMeridian", _root);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task IndexAsync_ResolvesConstructorParameterTypeToUsesEdge()
    {
        var service = WriteFile(
            "src/Service.cs",
            """
            namespace Demo.Services;

            using Demo.Data;

            public class ChainService
            {
                public ChainService(IChainRepository repository)
                {
                }
            }
            """);
        var repository = WriteFile(
            "src/IChainRepository.cs",
            """
            namespace Demo.Data;

            public interface IChainRepository
            {
                void Save();
            }
            """);
        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = new CSharpIndexer(client, NullLogger<CSharpIndexer>.Instance);

        await sut.IndexAsync([service, repository], "CodeMeridian", _root);

        var usesEdges = handler.Requests
            .Where(request => request.Path == "/api/v1/knowledge/nodes/edges"
                              && request.Body.GetProperty("type").GetString() == "Uses")
            .ToArray();

        usesEdges.Should().Contain(request =>
            request.Body.GetProperty("sourceId").GetString() == "CodeMeridian::Class::Demo.Services.ChainService"
            && request.Body.GetProperty("targetId").GetString() == "CodeMeridian::Interface::Demo.Data.IChainRepository");
        handler.Requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes"
            && request.Body.GetProperty("id").GetString() == "CodeMeridian::Method::Demo.Services.ChainService(IChainRepository)"
            && request.Body.GetProperty("type").GetString() == "Method");
    }

    [Fact]
    public async Task IndexAsync_ResolvesPropertyTypeToUsesEdge()
    {
        var service = WriteFile(
            "src/Service.cs",
            """
            namespace Demo.Services;

            using Demo.Data;

            public class ChainService
            {
                public ChainState State { get; set; } = new();
            }
            """);
        var state = WriteFile(
            "src/ChainState.cs",
            """
            namespace Demo.Data;

            public class ChainState
            {
            }
            """);
        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = new CSharpIndexer(client, NullLogger<CSharpIndexer>.Instance);

        await sut.IndexAsync([service, state], "CodeMeridian", _root);

        var usesEdges = handler.Requests
            .Where(request => request.Path == "/api/v1/knowledge/nodes/edges"
                              && request.Body.GetProperty("type").GetString() == "Uses")
            .ToArray();

        usesEdges.Should().Contain(request =>
            request.Body.GetProperty("sourceId").GetString() == "CodeMeridian::Class::Demo.Services.ChainService"
            && request.Body.GetProperty("targetId").GetString() == "CodeMeridian::Class::Demo.Data.ChainState");
    }

    [Fact]
    public async Task IndexAsync_ExtractsControllerAndMinimalApiRoutes()
    {
        var file = WriteFile(
            "src/Routes.cs",
            """
            using Microsoft.AspNetCore.Mvc;

            namespace Demo.Api;

            [Route("api/orders")]
            public class OrdersController : ControllerBase
            {
                [HttpGet("{id:int}")]
                public IActionResult Get(int id) => Ok();
            }

            public static class RouteRegistration
            {
                public static void Map(WebApplication app)
                {
                    app.MapGroup("/api").MapPost("/orders", HandleOrder);
                }

                private static IResult HandleOrder() => Results.Ok();
            }
            """);

        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = new CSharpIndexer(client, NullLogger<CSharpIndexer>.Instance);

        await sut.IndexAsync([file], "CodeMeridian", _root);

        handler.Requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes"
            && request.Body.GetProperty("id").GetString() == "CodeMeridian::ApiEndpoint::GET /api/orders/{param}"
            && request.Body.GetProperty("type").GetString() == "ApiEndpoint");

        handler.Requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes"
            && request.Body.GetProperty("id").GetString() == "CodeMeridian::ApiEndpoint::POST /api/orders"
            && request.Body.GetProperty("type").GetString() == "ApiEndpoint");

        var routeEdges = handler.Requests
            .Where(request => request.Path == "/api/v1/knowledge/nodes/edges"
                && request.Body.GetProperty("type").GetString() == "Uses")
            .ToArray();

        routeEdges.Should().Contain(edge =>
            edge.Body.GetProperty("sourceId").GetString() == "CodeMeridian::Method::Demo.Api.Get(int)"
            && edge.Body.GetProperty("targetId").GetString() == "CodeMeridian::ApiEndpoint::GET /api/orders/{param}"
            && edge.Body.GetProperty("callSite").GetString() == "src/Routes.cs:8");

        routeEdges.Should().Contain(edge =>
            edge.Body.GetProperty("sourceId").GetString() == "CodeMeridian::Method::Demo.Api.HandleOrder()"
            && edge.Body.GetProperty("targetId").GetString() == "CodeMeridian::ApiEndpoint::POST /api/orders"
            && edge.Body.GetProperty("callSite").GetString() == "src/Routes.cs:16");
    }

    private FileInfo WriteFile(string relativePath, string content)
    {
        var file = new FileInfo(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        file.Directory!.Create();
        File.WriteAllText(file.FullName, content);
        return file;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(string Method, string Path, JsonElement Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            Requests.Add((request.Method.Method, request.RequestUri!.AbsolutePath, doc.RootElement.Clone()));

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new { })
            };
        }
    }
}
