using System.Net;
using CodeMeridian.DocumentIndexer.Pipeline;
using CodeMeridian.Sdk;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeMeridian.DocumentIndexer.Tests.Pipeline;

public sealed class DocumentOutcomeTests
{
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
}
