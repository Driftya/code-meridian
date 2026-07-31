using CodeMeridian.Evolution.Application.Goals;
using CodeMeridian.Evolution.Application.Governance;
using CodeMeridian.Evolution.Application.Ledger;
using CodeMeridian.Evolution.Application.Observations;
using CodeMeridian.Evolution.Application.Reasoning;
using CodeMeridian.Evolution.Application.Sensors;
using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Api;

public static class EvolutionEndpoints
{
    private static readonly string[] FakeProviderRoles =
        ["planner", "researcher", "critic", "verifier", "summarizer"];

    public static IEndpointRouteBuilder MapEvolutionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/healthz", () => Results.Ok(new
        {
            status = "healthy",
            service = "Meridian Evolution"
        }));

        var api = endpoints.MapGroup("/api");

        MapLedgerEndpoints(api);
        MapGoalAndGovernanceEndpoints(api);
        MapSensorEndpoints(api);
        MapReasoningEndpoints(api);
        MapResearchEndpoints(api);
        api.MapEvolutionMindEndpoints();
        return endpoints;
    }

    private static void MapLedgerEndpoints(RouteGroupBuilder api)
    {
        api.MapGet("/now", (
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
            ledger.GetSnapshotAsync(cancellationToken));
        api.MapGet("/ledger/journal", (
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
            ledger.GetJournalAsync(cancellationToken));
        api.MapGet("/ledger/trial-balance", (
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
            ledger.GetTrialBalanceAsync(cancellationToken));
        api.MapGet("/ledger/accounts/{account}", async (
            string account,
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<LedgerAccount>(account, ignoreCase: true, out var parsed))
            {
                return Results.BadRequest(new { error = $"Unknown ledger account '{account}'." });
            }

            var snapshot = await ledger.GetSnapshotAsync(cancellationToken);
            return Results.Ok(snapshot.Accounts.Single(view => view.Account == parsed));
        });
        api.MapPost("/ledger/entries/{sequence:long}/challenge", (
            long sequence,
            CorrectionRequest request,
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
            ledger.ChallengeEntryAsync(sequence, request, cancellationToken));
        api.MapGet("/audit", (
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
            ledger.GetJournalAsync(cancellationToken));
        api.MapGet("/memories", (
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
            GetAccountAsync(ledger, LedgerAccount.Memory, cancellationToken));
        api.MapGet("/skills", (
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
            GetAccountAsync(ledger, LedgerAccount.Skill, cancellationToken));
        api.MapGet("/observations", (
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
            GetAccountAsync(ledger, LedgerAccount.Memory, cancellationToken));
        api.MapPost("/observations", (
            ObservationRequest request,
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
            ledger.RecordObservationAsync(request, cancellationToken));
    }

    private static void MapGoalAndGovernanceEndpoints(RouteGroupBuilder api)
    {
        api.MapGet("/goals", (
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
            GetGoalsAsync(ledger, cancellationToken));
        api.MapPost("/goals", (
            GoalRequest request,
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
            ledger.CreateGoalAsync(request, cancellationToken));
        api.MapPost("/goals/{goalId:guid}/pause", (
            Guid goalId,
            GovernanceCommand command,
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
            ledger.PauseGoalAsync(goalId, command, cancellationToken));
        api.MapGet("/governance", async (
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await ledger.GetSnapshotAsync(cancellationToken);
            return Results.Ok(new
            {
                snapshot.IsPaused,
                snapshot.AutonomyLevel,
                GovernanceKernel.ConstitutionVersion,
                GovernanceKernel.Principles
            });
        });
        api.MapPost("/governance/pause", (
            GovernanceCommand command,
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
            ledger.SetPausedAsync(isPaused: true, command, cancellationToken));
        api.MapPost("/governance/resume", (
            GovernanceCommand command,
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
            ledger.SetPausedAsync(isPaused: false, command, cancellationToken));
        api.MapGet("/self", async (
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
        {
            var identity = await GetAccountAsync(
                ledger,
                LedgerAccount.Identity,
                cancellationToken);
            return Results.Ok(new
            {
                name = "Meridian Evolution",
                purpose = "A separate persistent cognitive simulation with governed sensors, drives, memory, and model reasoning.",
                constitutionVersion = GovernanceKernel.ConstitutionVersion,
                principles = GovernanceKernel.Principles,
                identity
            });
        });
    }

    private static void MapSensorEndpoints(RouteGroupBuilder api)
    {
        api.MapGet("/sensors", async (
            SensorRegistry registry,
            CancellationToken cancellationToken) =>
        {
            var sensors = new List<object>();

            foreach (var sensor in registry.List())
            {
                var health = await sensor.CheckHealthAsync(cancellationToken);
                sensors.Add(new
                {
                    sensor.Id,
                    sensor.DisplayName,
                    health
                });
            }

            return Results.Ok(sensors);
        });
        api.MapPost("/sensors/{sensorId}/run", (
            string sensorId,
            SensorRunner runner,
            CancellationToken cancellationToken) =>
            runner.RunAsync(sensorId, cancellationToken));
    }

    private static void MapReasoningEndpoints(RouteGroupBuilder api)
    {
        api.MapGet("/reasoning/providers", (
            ReasoningRuntime runtime,
            CancellationToken cancellationToken) =>
            runtime.ProbeAllAsync(cancellationToken));
        api.MapGet("/reasoning/profiles", () => Results.Ok(new[]
        {
            new
            {
                id = "deterministic-read-only",
                providerId = "fake",
                roles = FakeProviderRoles,
                permissions = "read-only"
            },
            new
            {
                id = "configured-chat-read-only",
                providerId = "chat-model",
                roles = FakeProviderRoles,
                permissions = "read-only"
            }
        }));
        api.MapPost("/reasoning/invocations", (
            ReasoningRequest request,
            ReasoningRuntime runtime,
            CancellationToken cancellationToken) =>
            runtime.InvokeAsync(request, cancellationToken));
        api.MapPost("/reasoning/invocations/{invocationId:guid}/cancel", (
            Guid invocationId,
            string providerId,
            ReasoningRuntime runtime,
            CancellationToken cancellationToken) =>
            runtime.CancelAsync(providerId, invocationId, cancellationToken));
    }

    private static void MapResearchEndpoints(RouteGroupBuilder api)
    {
        api.MapGet("/signals", (
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
            GetAccountAsync(ledger, LedgerAccount.Research, cancellationToken));
        api.MapGet("/candidates", (
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
            GetAccountAsync(ledger, LedgerAccount.Action, cancellationToken));
        api.MapGet("/evaluations", (
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
            GetAccountAsync(ledger, LedgerAccount.System, cancellationToken));
        api.MapGet("/research/consciousness-claims", () => Results.Ok(new[]
        {
            new
            {
                id = "functional-continuity-v1",
                classification = "operational-hypothesis",
                statement = "Journal replay can preserve functional continuity across process restarts.",
                consciousnessClaim = false,
                status = "testable"
            }
        }));
    }

    private static async Task<object> GetAccountAsync(
        CognitiveLedgerService ledger,
        LedgerAccount account,
        CancellationToken cancellationToken)
    {
        var snapshot = await ledger.GetSnapshotAsync(cancellationToken);
        return snapshot.Accounts.Single(view => view.Account == account);
    }

    private static async Task<object> GetGoalsAsync(
        CognitiveLedgerService ledger,
        CancellationToken cancellationToken)
    {
        var snapshot = await ledger.GetSnapshotAsync(cancellationToken);
        return snapshot.ActiveGoals;
    }
}
