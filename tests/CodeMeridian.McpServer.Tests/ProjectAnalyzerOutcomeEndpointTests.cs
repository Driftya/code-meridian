using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.Core.Projects;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Sdk;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using ModelContextProtocol.Protocol;
using Json.Schema;

namespace CodeMeridian.McpServer.Tests;

public sealed class ProjectAnalyzerOutcomeEndpointTests
{
    [Fact]
    public async Task McpPlansDocumentEditsWithoutResolvingCodeSymbolsAndReturnsOutcomeFacts()
    {
        var repository = Substitute.For<IProjectProfileRepository>();
        var hash = "sha256:" + new string('a', 64);
        var fingerprint = new ProjectAnalyzerFingerprint(hash, hash);
        var profile = new ProjectProfile("Docs", DateTimeOffset.UtcNow, "complete", 1,
            [new("markdown", 1, ["chapter.md"])], [], 0,
            [new("documents", true, "enabled", "unknown", ["document_search"], []) { Fingerprint = fingerprint }], [], 0)
        { Revision = new(new string('a', 40), true), ConfigurationFingerprint = hash, SourceFingerprint = hash };
        repository.GetAsync("Docs", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(
            new StoredProjectProfile(1, 1, DateTimeOffset.UtcNow, profile, new("chapter.md", "markdown"))
            { AnalyzerOutcomes = [new("documents", ".", "succeeded", "complete", "full", DateTimeOffset.UtcNow, "cli/test", [])
                { Fingerprint = fingerprint, SourceFingerprint = hash }] });
        var vector = Substitute.For<IVectorRepository>();
        var graph = Substitute.For<ICodeGraphRepository>();
        graph.GetContextForEditingAsync("Docs::Class::Chapter", Arg.Any<CancellationToken>()).Returns(new EditingContext(
            new CodeNode { Id = "Docs::Class::Chapter", Name = "Chapter", Type = CodeNodeType.File, ProjectContext = "Docs", FilePath = "chapter.md" }, [], [], []));
        await using var factory = new GraphQlWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IProjectProfileRepository>(); services.AddSingleton(repository);
            services.RemoveAll<IVectorRepository>(); services.AddSingleton(vector);
            services.RemoveAll<ICodeGraphRepository>(); services.AddSingleton(graph);
        }));
        using var http = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(http);
        var plan = await client.CallToolAsync("plan_context_workflow", new Dictionary<string, object?>
            { ["goal"] = "Edit the chapter", ["target"] = "chapter.md", ["projectContext"] = "Docs" });
        plan.IsError.Should().NotBe(true);
        using var json = JsonDocument.Parse(plan.Content.OfType<TextContentBlock>().Single().Text);
        json.RootElement.GetProperty("workflowType").GetString().Should().Be("document_review");
        json.RootElement.GetProperty("applicability").GetProperty("status").GetString().Should().Be("ready");
        var facts = await client.CallToolAsync("get_project_profile", new Dictionary<string, object?> { ["projectContext"] = "Docs" });
        facts.StructuredContent!.Value.GetProperty("analyzerOutcomes")[0].GetProperty("coverage").GetString().Should().Be("complete");
        facts.StructuredContent.Value.GetProperty("profile").GetProperty("revision").GetProperty("dirty").GetBoolean().Should().BeTrue();
        facts.StructuredContent.Value.GetProperty("profile").GetProperty("configurationFingerprint").GetString().Should().Be(hash);
        facts.StructuredContent.Value.GetProperty("profile").GetProperty("sourceFingerprint").GetString().Should().Be(hash);
        facts.StructuredContent.Value.GetProperty("analyzerOutcomes")[0].GetProperty("sourceFingerprint").GetString().Should().Be(hash);
        facts.StructuredContent.Value.GetProperty("analyzerOutcomes")[0].GetProperty("fingerprint").GetProperty("parser").GetString().Should().Be(hash);
        facts.Content.OfType<TextContentBlock>().Single().Text.Should().Contain("parser fingerprint");
        var tool = (await client.ListToolsAsync()).Single(item => item.Name == "get_project_profile");
        JsonSchema.FromText(tool.ProtocolTool.OutputSchema!.Value.GetRawText()).Evaluate(facts.StructuredContent.Value).IsValid.Should().BeTrue();
        http.DefaultRequestHeaders.Authorization = new("Bearer", GraphQlWebApplicationFactory.ApiKey);
        var api = await new CodeMeridianClient(http).GetProjectProfileAsync("Docs");
        api.Profile!.Value.GetProperty("revision").GetProperty("commit").GetString().Should().Be(profile.Revision!.Commit);
        api.Profile.Value.GetProperty("sourceFingerprint").GetString().Should().Be(hash);
        var impact = await client.CallToolAsync("find_impact", new Dictionary<string, object?> { ["nodeId"] = "Docs::Class::Chapter" });
        impact.IsError.Should().NotBe(true);
        impact.StructuredContent!.Value.GetProperty("applicability").GetProperty("status").GetString().Should().Be("not_applicable");
        impact.Content.OfType<TextContentBlock>().Single().Text.Should().Contain("not_applicable").And.NotContain("No callers found");
        var impactTool = (await client.ListToolsAsync()).Single(item => item.Name == "find_impact");
        JsonSchema.FromText(impactTool.ProtocolTool.OutputSchema!.Value.GetRawText()).Evaluate(impact.StructuredContent.Value).IsValid.Should().BeTrue();
        await graph.DidNotReceiveWithAnyArgs().FindImpactAsync(default!, default, default);
    }

    [Fact]
    public async Task OutcomeApiRequiresAuthenticationValidatesAndForwardsSdkRequests()
    {
        var repository = Substitute.For<IProjectProfileRepository>();
        await using var factory = new GraphQlWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IProjectProfileRepository>(); services.AddSingleton(repository);
        }));
        using var http = factory.CreateClient();
        var snapshot = new ProjectAnalyzerOutcomeSnapshot("Docs", 1,
            [new("documents", ".", "succeeded", "complete", "full", DateTimeOffset.UtcNow, "cli/test", [])
                { Fingerprint = new("sha256:" + new string('a', 64), "sha256:" + new string('a', 64)),
                    SourceFingerprint = "sha256:" + new string('a', 64) }]);
        (await http.PostAsJsonAsync("/api/v1/project-profiles/outcomes", snapshot)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        http.DefaultRequestHeaders.Authorization = new("Bearer", GraphQlWebApplicationFactory.ApiKey);
        await new CodeMeridianClient(http).PublishProjectAnalyzerOutcomesAsync(snapshot);
        await repository.Received(1).PublishOutcomesAsync(Arg.Is<ProjectAnalyzerOutcomeSnapshot>(s => s.Generation == 1
            && s.Outcomes[0].Fingerprint == snapshot.Outcomes[0].Fingerprint
            && s.Outcomes[0].SourceFingerprint == snapshot.Outcomes[0].SourceFingerprint), Arg.Any<CancellationToken>());
        (await http.PostAsJsonAsync("/api/v1/project-profiles/outcomes", snapshot with {
            Outcomes = [snapshot.Outcomes[0] with { Mode = "delta" }]
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        repository.PublishOutcomesAsync(Arg.Any<ProjectAnalyzerOutcomeSnapshot>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new ProjectProfileConflictException("stale generation"));
        (await http.PostAsJsonAsync("/api/v1/project-profiles/outcomes", snapshot)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
