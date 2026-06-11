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
    public async Task IngestAsync_ResolvesRootAndRelativeDocumentReferencesFromMarkdown()
    {
        var document = WriteFile(
            "docs/architecture.md",
            """
            # Architecture

            See [payments implementation](../src/Payments/PaymentGateway.cs),
            [design note](./design-note.md),
            [feature backlog](../features/01-add-build-minimal-context.md),
            and [roadmap](/docs/features/34-add-safe-replacement-surface-guidance.md).

            Also reference `src/Orders/OrderService.ts` for the frontend integration path.
            """);

        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = new DocumentIndexerPipeline(client, NullLogger<DocumentIndexerPipeline>.Instance);

        await sut.IngestAsync([document], "DemoProject", _root);

        handler.Requests.Should().ContainSingle();
        var request = handler.Requests[0];

        request.Path.Should().Be("/api/v1/knowledge/documents");
        request.Body.GetProperty("relatedNodeIdsCsv").GetString().Should().Contain("DemoProject:File:src/Payments/PaymentGateway.cs");
        request.Body.GetProperty("relatedNodeIdsCsv").GetString().Should().Contain("DemoProject::File::src/Payments/PaymentGateway.cs");
        request.Body.GetProperty("relatedNodeIdsCsv").GetString().Should().Contain("DemoProject:File:src/Orders/OrderService.ts");
        HasDocumentReferences(
            request.Body.GetProperty("relatedDocumentIdsCsv").GetString(),
            "docs/design-note.md",
            "features/01-add-build-minimal-context.md",
            "docs/features/34-add-safe-replacement-surface-guidance.md").Should().BeTrue();
    }

    [Fact]
    public async Task IngestAsync_UsesSourceRelativeResolutionForTodoStyleRootLinks()
    {
        var document = WriteFile(
            "TODO.md",
            """
            - [Feature 1](docs/features/01-add-build-minimal-context.md)
            - [Feature 35](docs/features/35-add-knowledge-decay-graph.md)
            """);

        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = new DocumentIndexerPipeline(client, NullLogger<DocumentIndexerPipeline>.Instance);

        await sut.IngestAsync([document], "DemoProject", _root);

        handler.Requests.Should().ContainSingle(request =>
            request.Path == "/api/v1/knowledge/documents"
            && HasDocumentReferences(
                request.Body.GetProperty("relatedDocumentIdsCsv").GetString(),
                "docs/features/01-add-build-minimal-context.md",
                "docs/features/35-add-knowledge-decay-graph.md"));
    }

    [Fact]
    public async Task IngestAsync_InfersApiEndpointAndMcpToolMentions()
    {
        WriteFile(
            "src/McpServer/Tools/CodebaseTools.Analytics.cs",
            """
            [McpServerTool(Name = "find_connection")]
            public Task<string> FindConnectionAsync() => Task.FromResult(string.Empty);
            """);
        var document = WriteFile(
            "docs/plan.md",
            """
            Route to trace: POST /api/orders

            Tool surface:
            [McpServerTool(Name = "find_connection")]
            """);

        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = new DocumentIndexerPipeline(client, NullLogger<DocumentIndexerPipeline>.Instance);

        await sut.IngestAsync([document], "CodeMeridian", _root);

        handler.Requests.Should().ContainSingle();
        var relatedNodeIds = handler.Requests[0].Body.GetProperty("relatedNodeIdsCsv").GetString();
        relatedNodeIds.Should().Contain("CodeMeridian::ApiEndpoint::POST /api/orders");
        relatedNodeIds.Should().Contain("CodeMeridian:File:src/McpServer/Tools/CodebaseTools.Analytics.cs");
        relatedNodeIds.Should().Contain("CodeMeridian::File::src/McpServer/Tools/CodebaseTools.Analytics.cs");
    }

    [Fact]
    public async Task IngestAsync_AddsAdjacentChunkReferences()
    {
        var paragraphOne = new string('A', 3_900);
        var paragraphTwo = new string('B', 3_900);
        var document = WriteFile(
            "docs/large.md",
            $"""
            {paragraphOne}

            {paragraphTwo}

            [feature](./feature.md)
            """);

        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = new DocumentIndexerPipeline(client, NullLogger<DocumentIndexerPipeline>.Instance);

        await sut.IngestAsync([document], "DemoProject", _root);

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Body.GetProperty("id").GetString().Should().Be("DemoProject::doc::docs/large.md::part1");
        HasDocumentReferences(
            handler.Requests[0].Body.GetProperty("relatedDocumentIdsCsv").GetString(),
            "docs/feature.md",
            "DemoProject::doc::docs/large.md::part2").Should().BeTrue();

        handler.Requests[1].Body.GetProperty("id").GetString().Should().Be("DemoProject::doc::docs/large.md::part2");
        HasDocumentReferences(
            handler.Requests[1].Body.GetProperty("relatedDocumentIdsCsv").GetString(),
            "docs/feature.md",
            "DemoProject::doc::docs/large.md::part1").Should().BeTrue();
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

    private static bool HasDocumentReferences(string? csv, params string[] expected)
    {
        csv.Should().NotBeNull();
        var values = csv!
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return expected.All(values.Contains);
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
