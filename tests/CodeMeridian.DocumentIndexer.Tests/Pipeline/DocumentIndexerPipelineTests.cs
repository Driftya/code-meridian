using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.DocumentIndexer.Pipeline;
using CodeMeridian.Sdk;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeMeridian.DocumentIndexer.Tests.Pipeline;

public sealed class DocumentIndexerPipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-document-indexer-tests",
        Guid.NewGuid().ToString("N"));

    public DocumentIndexerPipelineTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task IngestAsync_InfersCodeAndDocumentReferencesFromMarkdown()
    {
        var document = WriteFile(
            "docs/architecture.md",
            """
            # Architecture

            See [payments implementation](../src/Payments/PaymentGateway.cs) and [design note](./design-note.md).

            Also reference `src/Orders/OrderService.ts` for the frontend integration path.
            """);

        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = new DocumentIndexerPipeline(client, NullLogger<DocumentIndexerPipeline>.Instance);

        await sut.IngestAsync([document], "DemoProject", _root);

        handler.Requests.Should().ContainSingle(request =>
            request.Path == "/api/v1/knowledge/documents"
            && request.Body.GetProperty("relatedNodeIdsCsv").GetString()!.Contains("DemoProject:File:src/Payments/PaymentGateway.cs")
            && request.Body.GetProperty("relatedNodeIdsCsv").GetString()!.Contains("DemoProject::File::src/Payments/PaymentGateway.cs")
            && request.Body.GetProperty("relatedNodeIdsCsv").GetString()!.Contains("DemoProject:File:src/Orders/OrderService.ts")
            && request.Body.GetProperty("relatedDocumentIdsCsv").GetString() == "design-note.md");
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
