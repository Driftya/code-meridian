using System.Net;
using System.Net.Http.Json;
using CodeMeridian.Sdk;
using FluentAssertions;

namespace CodeMeridian.Sdk.Tests;

public sealed class RepositoryConstraintClientTests
{
    [Fact]
    public async Task QueryEncodesScopeAndUsesTheAuthenticatedClient()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            request.Headers.Authorization!.Parameter.Should().Be("test-key");
            request.RequestUri!.PathAndQuery.Should().Be("/api/v1/repository-constraints?projectContext=Docs%20%26%20Notes&findingLimit=7&targetPath=docs%2Fa%20%26%20b.md");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            {
                project = "Docs & Notes", status = "incomplete", coverage = "unknown", diagnostics = Array.Empty<object>(), contractVersion = "1.0"
            }) });
        })) { BaseAddress = new Uri("http://localhost") };
        http.DefaultRequestHeaders.Authorization = new("Bearer", "test-key");
        var result = await new CodeMeridianClient(http).FindRepositoryConstraintViolationsAsync("Docs & Notes", "docs/a & b.md", 7);
        result.Status.Should().Be("incomplete");
        result.Evaluation.Should().BeNull();
    }

    [Fact]
    public async Task HttpFailureAndCancellationPropagate()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest))))
            { BaseAddress = new Uri("http://localhost") };
        var sdk = new CodeMeridianClient(http);
        await FluentActions.Awaiting(() => sdk.FindRepositoryConstraintViolationsAsync("Docs")).Should().ThrowAsync<HttpRequestException>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await FluentActions.Awaiting(() => sdk.FindRepositoryConstraintViolationsAsync("Docs", cancellationToken: cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return send(request, cancellationToken);
        }
    }
}
