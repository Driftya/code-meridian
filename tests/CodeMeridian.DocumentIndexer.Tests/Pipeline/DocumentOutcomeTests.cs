using System.Net;
using CodeMeridian.DocumentIndexer.Pipeline;
using CodeMeridian.Sdk;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeMeridian.DocumentIndexer.Tests.Pipeline;

public sealed class DocumentOutcomeTests
{
    [Fact]
    public async Task VerifiedReaderSuppliesBothDocumentAndToolReferenceBytes()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"document-source-{Guid.NewGuid():N}"));
        try
        {
            var document = new FileInfo(Path.Combine(root.FullName, "chapter.md"));
            var tool = new FileInfo(Path.Combine(root.FullName, "src", "McpServer", "Tools", "Tools.cs"));
            tool.Directory!.Create();
            File.WriteAllText(document.FullName, "unverified document bytes");
            File.WriteAllText(tool.FullName, "unverified tool bytes");
            var reads = new List<string>();
            using var http = new HttpClient(new SuccessHandler()) { BaseAddress = new Uri("http://localhost") };
            var pipeline = new DocumentIndexerPipeline(new CodeMeridianClient(http), NullLogger<DocumentIndexerPipeline>.Instance);
            var stats = await pipeline.IngestAsync([document], "Docs", root.FullName, readContent: (file, _) =>
            {
                reads.Add(file.FullName);
                return Task.FromResult(file.FullName == document.FullName ? "# Verified document" : "[McpServerTool(Name = \"verified_tool\")]");
            });
            stats.FailedFiles.Should().Be(0);
            stats.DocumentsIndexed.Should().Be(1);
            reads.Should().Equal(tool.FullName, document.FullName);
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public async Task FailedUploadIsCountedAndCancellationIsNotSwallowed()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"document-outcome-{Guid.NewGuid():N}"));
        try
        {
            var path = Path.Combine(root.FullName, "chapter.md");
            await File.WriteAllTextAsync(path, "# Chapter");
            using var http = new HttpClient(new Handler()) { BaseAddress = new Uri("http://localhost") };
            var pipeline = new DocumentIndexerPipeline(new CodeMeridianClient(http), NullLogger<DocumentIndexerPipeline>.Instance);
            var result = await pipeline.IngestAsync([new(path)], "Docs", root.FullName);
            result.FailedFiles.Should().Be(1);
            result.DocumentsIndexed.Should().Be(0);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var act = () => pipeline.IngestAsync([new(path)], "Docs", root.FullName, cancellation.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally { root.Delete(recursive: true); }
    }

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private sealed class SuccessHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            payload.Should().Contain("Verified document").And.NotContain("unverified");
            return new(HttpStatusCode.OK);
        }
    }
}
