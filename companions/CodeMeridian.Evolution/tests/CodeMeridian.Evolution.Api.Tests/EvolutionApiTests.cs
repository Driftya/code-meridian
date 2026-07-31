using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeMeridian.Evolution.Application.Goals;
using CodeMeridian.Evolution.Application.Governance;
using CodeMeridian.Evolution.Application.Cognition;
using CodeMeridian.Evolution.Application.Projects;
using CodeMeridian.Evolution.Application.Projections;
using CodeMeridian.Evolution.Application.Sensors;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CodeMeridian.Evolution.Api.Tests;

public sealed class EvolutionApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public EvolutionApiTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
        client = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Evolution:Storage:UseInMemory", "true"))
            .CreateClient();
    }

    [Fact]
    public async Task NowReturnsInitializedBalancedLedger()
    {
        var response = await client.GetAsync(
            "/api/now",
            CancellationToken.None);

        response.EnsureSuccessStatusCode();
        var snapshot = await response.Content.ReadFromJsonAsync<CognitiveSnapshot>(
            JsonOptions,
            CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.IsBalanced);
        Assert.NotEmpty(snapshot.HeadHash);
        Assert.True(snapshot.EntryCount >= 1);
    }

    [Fact]
    public async Task GoalIntakeIsIdempotent()
    {
        var request = new GoalRequest(
            Guid.NewGuid(),
            "Verify standalone continuity",
            "api-test",
            "The ledger replays to the same projection.",
            null,
            0m,
            $"api-test:{Guid.NewGuid():D}");

        var first = await client.PostAsJsonAsync(
            "/api/goals",
            request,
            CancellationToken.None);
        var repeated = await client.PostAsJsonAsync(
            "/api/goals",
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        var goals = await client.GetFromJsonAsync<LedgerItemView[]>(
            "/api/goals",
            JsonOptions,
            CancellationToken.None);
        Assert.Contains(goals!, goal => goal.SubjectId == request.Id.ToString("D"));
    }

    [Fact]
    public async Task ConfiguredMutationKeyIsRequired()
    {
        using var securedClient = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Evolution:Storage:UseInMemory", "true");
            builder.UseSetting("Evolution:ApiKey", "required-test-key");
        }).CreateClient();
        var request = new GoalRequest(
            Guid.NewGuid(),
            "Unauthorized goal",
            "api-test",
            "This request must not reach the ledger.",
            null,
            0m,
            $"api-test:unauthorized:{Guid.NewGuid():D}");

        var response = await securedClient.PostAsJsonAsync(
            "/api/goals",
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GovernancePauseRejectsNewGoalsWithConflict()
    {
        using var isolatedClient = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Evolution:Storage:UseInMemory", "true"))
            .CreateClient();
        var pause = await isolatedClient.PostAsJsonAsync(
            "/api/governance/pause",
            new GovernanceCommand(
                "api-test",
                "Verify the operational pause boundary.",
                $"pause:{Guid.NewGuid():D}"),
            CancellationToken.None);
        pause.EnsureSuccessStatusCode();
        var request = new GoalRequest(
            Guid.NewGuid(),
            "Blocked goal",
            "api-test",
            "This request must be rejected while paused.",
            null,
            0m,
            $"api-test:paused:{Guid.NewGuid():D}");

        var response = await isolatedClient.PostAsJsonAsync(
            "/api/goals",
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PromptCanDriveASimulatedCandidateThroughHumanApproval()
    {
        using var isolatedClient = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Evolution:Storage:UseInMemory", "true"))
            .CreateClient();
        var prompt = await isolatedClient.PostAsJsonAsync(
            "/api/perception/prompts",
            new PromptInput(
                "Inspect a suspected CodeMeridian test gap.",
                "human:api-test",
                "codemeridian",
                $"prompt:{Guid.NewGuid():D}"),
            CancellationToken.None);
        prompt.EnsureSuccessStatusCode();
        var cycle = await isolatedClient.PostAsJsonAsync(
            "/api/mind/cycles",
            new CognitiveCycleRequest(
                "fake",
                "researcher",
                "codemeridian",
                null,
                8,
                Force: false),
            CancellationToken.None);
        cycle.EnsureSuccessStatusCode();
        using var cycleJson = JsonDocument.Parse(
            await cycle.Content.ReadAsStringAsync(CancellationToken.None));
        Assert.Equal(
            "CandidateProposed",
            cycleJson.RootElement.GetProperty("status").GetString());

        var mind = await isolatedClient.GetAsync(
            "/api/mind?projectId=codemeridian",
            CancellationToken.None);
        mind.EnsureSuccessStatusCode();
        using var mindJson = JsonDocument.Parse(
            await mind.Content.ReadAsStringAsync(CancellationToken.None));
        var candidate = Assert.Single(
            mindJson.RootElement.GetProperty("candidates").EnumerateArray());
        var candidateId = candidate.GetProperty("subjectId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(candidateId));

        var approval = await isolatedClient.PostAsJsonAsync(
            $"/api/candidates/{Uri.EscapeDataString(candidateId!)}/approve",
            new CandidateApprovalRequest(
                "human:api-test",
                "Approved for isolated preparation only.",
                $"approval:{Guid.NewGuid():D}"),
            CancellationToken.None);

        approval.EnsureSuccessStatusCode();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
