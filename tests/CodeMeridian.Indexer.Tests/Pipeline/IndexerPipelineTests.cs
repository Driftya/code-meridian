using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Sdk;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeMeridian.Indexer.Tests.Pipeline;

public sealed class IndexerPipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-indexer-pipeline-tests",
        Guid.NewGuid().ToString("N"));

    public IndexerPipelineTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task RunAsync_FullNonClearScan_DoesNotDeleteBeforeUpserting()
    {
        WriteFile("src/App.cs", "namespace Demo; public class App { public void Run() {} }");
        WriteFile("docs/guide.md", "Project guide");
        WriteFile("node_modules/pkg/README.md", "Dependency docs should not be indexed");

        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = BuildPipeline(client);

        await sut.RunAsync(new DirectoryInfo(_root), "CodeMeridian", clear: false, includeDocs: true);

        handler.Requests.Should().NotContain(request => request.Method == "DELETE");
        var documentSources = handler.Requests
            .Where(request => request.Path == "/api/v1/knowledge/documents")
            .Select(request => request.Body.GetProperty("source").GetString())
            .ToArray();

        documentSources.Should().ContainSingle("docs/guide.md");
        documentSources
            .Any(source => source != null && source.Contains("node_modules", StringComparison.OrdinalIgnoreCase))
            .Should()
            .BeFalse();
    }

    private IndexerPipeline BuildPipeline(CodeMeridianClient client) =>
        new(
            new CSharpIndexer(client, NullLogger<CSharpIndexer>.Instance),
            new DocumentIngester(client, NullLogger<DocumentIngester>.Instance),
            client,
            NullLogger<IndexerPipeline>.Instance);

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
