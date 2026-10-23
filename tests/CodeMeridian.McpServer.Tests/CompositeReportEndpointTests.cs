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

public sealed class CompositeReportEndpointTests
{
    [Fact]
    public async Task McpHonorsDisabledSectionsAndBlocksRequestedUnavailableTests()
    {
        var profiles = Substitute.For<IProjectProfileRepository>();
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Stored());
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        await using var factory = Factory(profiles, graph, vector);
        using var http = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(http);
        var arguments = new Dictionary<string, object?>
        {
            ["feature"] = "Feature", ["projectContext"] = "Fixture", ["includeTests"] = false, ["includeDocs"] = false
        };

        var result = await client.CallToolAsync("analyze_feature_implementation_path", arguments);

        result.IsError.Should().NotBe(true);
        result.Content.OfType<TextContentBlock>().Should().ContainSingle().Which.Text.Should().Contain("status: ok");
        await graph.Received().QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>());
        await graph.DidNotReceiveWithAnyArgs().FindRelatedTestsAsync(default!, default, default);
        await vector.DidNotReceiveWithAnyArgs().SearchByTextAsync(default!, default, default, default);
        graph.ClearReceivedCalls();
        arguments["includeTests"] = true;

        var blocked = await client.CallToolAsync("analyze_feature_implementation_path", arguments);

        blocked.Content.OfType<TextContentBlock>().Should().ContainSingle().Which.Text.Should()
            .Contain("status: unknown").And.Contain("test_to_code");
        await graph.DidNotReceiveWithAnyArgs().QueryNodesAsync(default!, default);
    }

    [Fact]
    public async Task PrContextSdkRoundTripsCoverageFromTheRealApiService()
    {
        var profiles = Substitute.For<IProjectProfileRepository>();
        var stored = Stored();
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored with
        {
            Profile = stored.Profile with { Analyzers = [stored.Profile.Analyzers[0] with
                { SupportedCapabilities = ["callable_definitions", "callable_dependencies", "test_to_code", "change_history"] }] }
        });
        await using var factory = Factory(profiles, Substitute.For<ICodeGraphRepository>(), Substitute.For<IVectorRepository>());
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", GraphQlWebApplicationFactory.ApiKey);
        var sdk = new CodeMeridianClient(http);

        var available = await sdk.BuildPrContextReportAsync(new(["src/A.cs"], "Fixture", IncludeDocs: false));
        var blocked = await sdk.BuildPrContextReportAsync(new(["src/A.cs"], "Fixture", IncludeDocs: true));

        available!.Applicability!.Status.Should().Be("ok");
        available.Applicability.ProfileGeneration.Should().Be(7);
        available.Applicability.Coverage.Should().Be("complete");
        blocked!.Applicability!.Status.Should().Be("unknown");
        blocked.Applicability.Coverage.Should().Be("unknown");
        blocked.Applicability.Reasons.Should().Contain(reason => reason.Contains("documents"));
        blocked.ChangedNodes.Should().BeEmpty();
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory(IProjectProfileRepository profiles,
        ICodeGraphRepository graph, IVectorRepository vector) => new GraphQlWebApplicationFactory().WithWebHostBuilder(builder =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IProjectProfileRepository>();
            services.RemoveAll<ICodeGraphRepository>();
            services.RemoveAll<IVectorRepository>();
            services.AddSingleton(profiles);
            services.AddSingleton(graph);
            services.AddSingleton(vector);
        }));

    private static StoredProjectProfile Stored() => new(7, 7, DateTimeOffset.UtcNow,
        new("Fixture", DateTimeOffset.UtcNow, "complete", 1, [new("csharp", 1, ["src/A.cs"])], [], 0,
            [new("csharp", true, "enabled", "unknown", ["callable_definitions", "callable_dependencies"], [])], [], 0)
            { SourceFingerprint = "sha256:" + new string('a', 64) }, null)
    {
        AnalyzerOutcomes = [new("csharp", ".", "succeeded", "complete", "full", DateTimeOffset.UtcNow, "cli/test", [])
            { SourceFingerprint = "sha256:" + new string('a', 64) }]
    };
}
