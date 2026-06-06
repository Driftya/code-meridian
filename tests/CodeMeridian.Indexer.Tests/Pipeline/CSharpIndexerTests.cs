using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Sdk;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeMeridian.Indexer.Tests.Pipeline;

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
