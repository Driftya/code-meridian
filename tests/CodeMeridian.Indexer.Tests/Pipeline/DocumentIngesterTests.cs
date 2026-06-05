using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Sdk;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeMeridian.Indexer.Tests.Pipeline;

public sealed class DocumentIngesterTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-document-ingester-tests",
        Guid.NewGuid().ToString("N"));

    public DocumentIngesterTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task IngestAsync_SplitsCrLfParagraphsWithoutBreakingBoundaries()
    {
        var firstParagraph = new string('A', 2300);
        var secondParagraph = new string('B', 2300);
        var file = WriteFile("docs", "guide.md", string.Join("\r\n\r\n", [firstParagraph, secondParagraph]));
        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = new DocumentIngester(client, NullLogger<DocumentIngester>.Instance);

        var stats = await sut.IngestAsync([file], "CodeMeridian", _root);

        stats.Documents.Should().Be(2);
        handler.Requests.Should().HaveCount(2);

        var first = handler.Requests[0];
        var second = handler.Requests[1];

        first.GetProperty("id").GetString().Should().Be("CodeMeridian::doc::docs/guide.md::part1");
        second.GetProperty("id").GetString().Should().Be("CodeMeridian::doc::docs/guide.md::part2");
        first.GetProperty("source").GetString().Should().Be("docs/guide.md");
        second.GetProperty("source").GetString().Should().Be("docs/guide.md");
        first.GetProperty("content").GetString().Should().Be(firstParagraph);
        second.GetProperty("content").GetString().Should().Be(secondParagraph);
    }

    [Fact]
    public async Task IngestAsync_SkipsWhitespaceOnlyFiles()
    {
        var file = WriteFile("docs", "empty.md", "   \r\n\t  ");
        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = new DocumentIngester(client, NullLogger<DocumentIngester>.Instance);

        var stats = await sut.IngestAsync([file], "CodeMeridian", _root);

        stats.Documents.Should().Be(0);
        handler.Requests.Should().BeEmpty();
    }

    private FileInfo WriteFile(string relativeDirectory, string fileName, string content)
    {
        var directory = Path.Combine(_root, relativeDirectory);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content);
        return new FileInfo(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<JsonElement> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            Requests.Add(doc.RootElement.Clone());

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new { })
            };
        }
    }
}
