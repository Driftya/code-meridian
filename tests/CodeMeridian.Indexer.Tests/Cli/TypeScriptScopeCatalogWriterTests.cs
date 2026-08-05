using System.Net;
using System.Text.Json;
using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class TypeScriptScopeCatalogWriterTests
{
    [Fact]
    public async Task WriteAsync_PersistsSortedNormalizedActiveScopes()
    {
        var handler = new RecordingHandler();
        var sut = new TypeScriptScopeCatalogWriter((url, _) => new HttpClient(handler)
        {
            BaseAddress = new Uri(url)
        });
        var basePath = Path.Combine(Path.GetTempPath(), "scope-catalog");

        await sut.WriteAsync(
            "Project",
            "http://localhost/",
            null,
            [
                new DirectoryInfo(Path.Combine(basePath, "web-b")),
                new DirectoryInfo(Path.Combine(basePath, "web-a")),
                new DirectoryInfo(Path.Combine(basePath, "web-a"))
            ]);

        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("id").GetString()
            .Should().Be("Project::IndexScopeCatalog::typescript");
        var properties = body.RootElement.GetProperty("properties");
        properties.GetProperty("externalKind").GetString().Should().Be("IndexRun");
        properties.GetProperty("indexRunKind").GetString().Should().Be("IndexScopeCatalog");
        var scopes = JsonSerializer.Deserialize<string[]>(properties.GetProperty("resolutionScopes").GetString()!);
        scopes.Should().Equal(
            Path.GetFullPath(Path.Combine(basePath, "web-a")).Replace('\\', '/'),
            Path.GetFullPath(Path.Combine(basePath, "web-b")).Replace('\\', '/'));
    }

    [Fact]
    public async Task WriteAsync_WithNoRoots_PersistsEmptyCatalog()
    {
        var handler = new RecordingHandler();
        var sut = new TypeScriptScopeCatalogWriter((url, _) => new HttpClient(handler)
        {
            BaseAddress = new Uri(url)
        });

        await sut.WriteAsync("Project", "http://localhost/", null, []);

        using var body = JsonDocument.Parse(handler.Body!);
        var scopesJson = body.RootElement.GetProperty("properties").GetProperty("resolutionScopes").GetString();
        JsonSerializer.Deserialize<string[]>(scopesJson!).Should().BeEmpty();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
