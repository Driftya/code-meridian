using System.Net;
using CodeMeridian.Sdk;
using FluentAssertions;

namespace CodeMeridian.Sdk.Tests;

public sealed class DocumentSearchClientTests
{
    [Fact]
    public async Task ScopedSearchEncodesQueryProjectAndExactCasePathsAndUsesAuthentication()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            request.Headers.Authorization!.Parameter.Should().Be("test-key");
            request.RequestUri!.PathAndQuery.Should().Be("/api/v1/knowledge/documents/search?query=chapter%20%26%20image&projectContext=Docs%20%26%20Notes&rootPath=docs%2Fdesign%20notes&targetPath=docs%2Fdesign%20notes%2FA.md");
            return new(HttpStatusCode.OK) { Content = new StringContent("Document search status: ok. Scope: docs/design notes/A.md.") };
        })) { BaseAddress = new Uri("http://localhost") };
        http.DefaultRequestHeaders.Authorization = new("Bearer", "test-key");

        var result = await new CodeMeridianClient(http).SearchDocumentationAsync("chapter & image", "Docs & Notes",
            "docs/design notes", "docs/design notes/A.md");

        result.Should().Contain("status: ok").And.Contain("A.md");
    }

    [Fact]
    public async Task ValidationErrorsAreNotReturnedAsSuccessfulSearchText()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.BadRequest))) { BaseAddress = new Uri("http://localhost") };
        var action = () => new CodeMeridianClient(http).SearchDocumentationAsync("chapter", "Docs", "../docs");
        await action.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        using var http = new HttpClient(new Handler(_ => throw new OperationCanceledException())) { BaseAddress = new Uri("http://localhost") };
        var action = () => new CodeMeridianClient(http).SearchDocumentationAsync("chapter", "Docs", "docs");
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
