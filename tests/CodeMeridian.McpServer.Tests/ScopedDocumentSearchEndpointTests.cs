using System.Net;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.Core.Projects;
using CodeMeridian.Sdk;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Protocol;
using NSubstitute;

namespace CodeMeridian.McpServer.Tests;

public sealed class ScopedDocumentSearchEndpointTests
{
    private readonly IProjectProfileRepository profiles = Substitute.For<IProjectProfileRepository>();
    private readonly IVectorRepository vector = Substitute.For<IVectorRepository>();

    [Fact]
    public async Task ApiRequiresAuthenticationAndSdkCarriesTheScopeThroughTheRealService()
    {
        Publish();
        await using var factory = Factory();
        using var http = factory.CreateClient();
        (await http.GetAsync("/api/v1/knowledge/documents/search?query=chapter&projectContext=Docs&rootPath=docs"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        http.DefaultRequestHeaders.Authorization = new("Bearer", GraphQlWebApplicationFactory.ApiKey);

        var result = await new CodeMeridianClient(http).SearchDocumentationAsync("chapter", "Docs", "docs", "docs/A.md");

        result.Should().Contain("Scope: docs/A.md.").And.Contain("current chapter").And.NotContain("outside chapter");
        await vector.Received(1).SearchByTextInScopeAsync("chapter", "Docs",
            Arg.Is<DocumentQueryScope>(scope => scope.RootPath == "docs" && scope.TargetPath == "docs/A.md"), 8, Arg.Any<CancellationToken>());
        await vector.DidNotReceiveWithAnyArgs().SearchByTextAsync(default!, default, default, default);
    }

    [Theory]
    [InlineData("rootPath=..%2Fdocs&projectContext=Docs")]
    [InlineData("rootPath=docs&targetPath=other%2FA.md&projectContext=Docs")]
    [InlineData("rootPath=docs")]
    public async Task ApiRejectsInvalidOrUnboundScopeBeforeStorage(string scopeQuery)
    {
        await using var factory = Factory();
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", GraphQlWebApplicationFactory.ApiKey);

        (await http.GetAsync("/api/v1/knowledge/documents/search?query=chapter&" + scopeQuery)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await vector.DidNotReceiveWithAnyArgs().SearchByTextInScopeAsync(default!, default!, default!, default, default);
    }

    [Fact]
    public async Task McpAdvertisesScopeAndKeepsUnfilteredFallbackConservative()
    {
        Publish();
        await using var factory = Factory();
        using var http = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(http);
        var tool = (await client.ListToolsAsync()).Single(item => item.Name == "search_documentation");
        var properties = tool.ProtocolTool.InputSchema.GetProperty("properties");
        properties.TryGetProperty("rootPath", out _).Should().BeTrue();
        properties.TryGetProperty("targetPath", out _).Should().BeTrue();
        var arguments = new Dictionary<string, object?> { ["query"] = "chapter", ["projectContext"] = "Docs", ["rootPath"] = "docs" };

        var scoped = await client.CallToolAsync("search_documentation", arguments);
        scoped.IsError.Should().NotBe(true);
        scoped.Content.OfType<TextContentBlock>().Should().ContainSingle().Which.Text.Should()
            .Contain("Scope: docs.").And.Contain("current chapter").And.NotContain("outside chapter");
        vector.ClearReceivedCalls();
        arguments.Remove("rootPath");
        var unfiltered = await client.CallToolAsync("search_documentation", arguments);
        unfiltered.Content.OfType<TextContentBlock>().Should().ContainSingle().Which.Text.Should().Contain("status: unknown");
        await vector.DidNotReceiveWithAnyArgs().SearchByTextAsync(default!, default, default, default);
    }

    private void Publish()
    {
        var stored = new StoredProjectProfile(1, 1, DateTimeOffset.UtcNow,
            new("Docs", DateTimeOffset.UtcNow, "complete", 1, [new("markdown", 1, ["docs/A.md"])], [], 0,
                [new("documents", true, "enabled", "unknown", ["document_search"], [])], [], 0)
                { SourceFingerprint = "sha256:" + new string('a', 64) }, null)
        { AnalyzerOutcomes = [new("documents", "docs", "succeeded", "complete", "full", DateTimeOffset.UtcNow, "cli/test", [])
            { SourceFingerprint = "sha256:" + new string('a', 64) }] };
        profiles.GetAsync("Docs", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored);
        profiles.GetAsync("Docs", "docs/A.md", Arg.Any<CancellationToken>()).Returns(stored with { Target = new("docs/A.md", "markdown") });
        vector.SearchByTextInScopeAsync("chapter", "Docs", Arg.Any<DocumentQueryScope>(), 8, Arg.Any<CancellationToken>()).Returns(
            [new KnowledgeDocument { Id = "one", Source = "docs/A.md", ProjectContext = "Docs", Content = "current chapter" },
             new KnowledgeDocument { Id = "other", Source = "other/A.md", ProjectContext = "Docs", Content = "outside chapter" }]);
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory() => new GraphQlWebApplicationFactory()
        .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IProjectProfileRepository>();
            services.RemoveAll<ICodeGraphRepository>();
            services.RemoveAll<IVectorRepository>();
            services.AddSingleton(profiles);
            services.AddSingleton(Substitute.For<ICodeGraphRepository>());
            services.AddSingleton(vector);
        }));
}
