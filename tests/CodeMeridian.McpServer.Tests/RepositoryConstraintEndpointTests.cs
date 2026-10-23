using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CodeMeridian.Core.Projects;
using CodeMeridian.Core.RepositoryConstraints;
using CodeMeridian.Sdk;
using FluentAssertions;
using Json.Schema;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Protocol;
using NSubstitute;

namespace CodeMeridian.McpServer.Tests;

public sealed class RepositoryConstraintEndpointTests
{
    [Fact]
    public async Task AuthenticatedApiAndSdkRoundTripPlacementEvidenceAndRejectInvalidRequests()
    {
        var repository = Repository();
        await using var factory = new GraphQlWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IProjectProfileRepository>();
            services.AddSingleton(repository);
        }));
        using var http = factory.CreateClient();
        const string endpoint = "/api/v1/repository-constraints?projectContext=Docs&targetPath=wrong.md&findingLimit=1";
        (await http.GetAsync(endpoint)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        http.DefaultRequestHeaders.Authorization = new("Bearer", GraphQlWebApplicationFactory.ApiKey);
        var result = await new CodeMeridianClient(http).FindRepositoryConstraintViolationsAsync("Docs", "wrong.md", 1);
        result.ContractVersion.Should().Be("1.0");
        result.Status.Should().Be("evaluated");
        result.Coverage.Should().Be("complete");
        result.Evaluation!.TargetStatus.Should().Be("observed");
        var rule = result.Evaluation.Rules.Should().ContainSingle().Subject;
        rule.RuleId.Should().Be("location");
        rule.RequiredCapabilities.Should().Equal("repository_inventory");
        rule.Findings.Should().ContainSingle().Which.Path.Should().Be("wrong.md");
        rule.Findings[0].Line.Should().BeNull();
        rule.Reason.Should().Be("Keep Markdown in docs.");
        (await http.GetAsync(endpoint.Replace("wrong.md", "..%2Foutside.md"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await http.GetAsync(endpoint.Replace("findingLimit=1", "findingLimit=1001"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await http.GetAsync("/api/v1/repository-constraints?projectContext=%20Docs")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task McpAdvertisesReadOnlyClosedWorldSchemaAndReturnsSameFactsAndText()
    {
        var repository = Repository();
        await using var factory = new GraphQlWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IProjectProfileRepository>();
            services.AddSingleton(repository);
        }));
        using var http = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(http);
        var tool = (await client.ListToolsAsync()).Single(tool => tool.Name == "find_repository_constraint_violations");
        tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue();
        tool.ProtocolTool.Annotations.DestructiveHint.Should().BeFalse();
        tool.ProtocolTool.Annotations.IdempotentHint.Should().BeTrue();
        tool.ProtocolTool.Annotations.OpenWorldHint.Should().BeFalse();
        var schema = JsonSchema.FromText(tool.ProtocolTool.OutputSchema!.Value.GetRawText());
        foreach (var project in new[] { "Docs", "Unknown" })
        {
            var result = await client.CallToolAsync(tool.Name, new Dictionary<string, object?>
                { ["projectContext"] = project, ["findingLimit"] = 1 });
            result.IsError.Should().NotBe(true);
            schema.Evaluate(result.StructuredContent!.Value).IsValid.Should().BeTrue();
            result.Content.OfType<TextContentBlock>().Should().ContainSingle().Which.Text
                .Should().Contain(project == "Docs" ? "misplaced" : "missing_inventory");
        }
        var invalid = await client.CallToolAsync(tool.Name, new Dictionary<string, object?>
            { ["projectContext"] = "Docs", ["findingLimit"] = 0 });
        invalid.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ProfilePublicationAcceptsValidatedOptionalEvidenceAndRejectsUnboundEvidence()
    {
        var repository = Repository();
        var stored = await repository.GetRepositoryConstraintsAsync("Docs");
        var snapshot = new ProjectProfileSnapshot(4, stored!.Profile, stored.Files) { ConstraintEvidence = stored.Evidence };
        await using var factory = new GraphQlWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IProjectProfileRepository>();
            services.AddSingleton(repository);
        }));
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", GraphQlWebApplicationFactory.ApiKey);
        (await http.PostAsJsonAsync("/api/v1/project-profiles/", snapshot)).StatusCode.Should().Be(HttpStatusCode.OK);
        await repository.Received(1).PublishAsync(Arg.Is<ProjectProfileSnapshot>(value => value.ConstraintEvidence != null), Arg.Any<CancellationToken>());
        (await http.PostAsJsonAsync("/api/v1/project-profiles/", snapshot with { Profile = snapshot.Profile with { SourceFingerprint = null } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (var invalid in new[]
        {
            json.Replace("\"groups\":", "\"customValidators\":[],\"groups\":", StringComparison.Ordinal),
            json.Replace("\"caseSensitive\":true", "\"caseSensitive\":true,\"caseSensitive\":false", StringComparison.Ordinal)
        })
        {
            using var body = new StringContent(invalid, Encoding.UTF8, "application/json");
            (await http.PostAsync("/api/v1/project-profiles/", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        await repository.Received(1).PublishAsync(Arg.Any<ProjectProfileSnapshot>(), Arg.Any<CancellationToken>());
    }

    private static IProjectProfileRepository Repository()
    {
        var hash = "sha256:" + new string('a', 64);
        var profile = new ProjectProfile("Docs", DateTimeOffset.UtcNow, "complete", 2,
            [new("markdown", 1, ["wrong.md"]), new("configuration", 1, ["policy.json"])], [], 0, [], [], 0)
            { SourceFingerprint = hash, ConfigurationFingerprint = hash };
        var evidence = new RepositoryConstraintEvidence("policy.json", hash, true,
            new("1.0", [new("documents", ["**/*.md"])],
                [new("location", "documents", ["docs/**"], [], "Keep Markdown in docs.", "error", [])], []), []);
        var repository = Substitute.For<IProjectProfileRepository>();
        repository.GetRepositoryConstraintsAsync("Docs", Arg.Any<CancellationToken>()).Returns(new StoredRepositoryConstraintSnapshot(4, 4,
            profile, [new("wrong.md", "markdown"), new("policy.json", "configuration")], evidence));
        repository.GetAsync("Docs", cancellationToken: Arg.Any<CancellationToken>()).Returns(new StoredProjectProfile(4, 4, DateTimeOffset.UtcNow, profile, null));
        return repository;
    }
}
