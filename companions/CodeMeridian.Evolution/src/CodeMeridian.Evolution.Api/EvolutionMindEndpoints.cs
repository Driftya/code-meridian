using CodeMeridian.Evolution.Application.Cognition;
using CodeMeridian.Evolution.Application.Ledger;
using CodeMeridian.Evolution.Application.Projects;
using CodeMeridian.Evolution.Application.Sensors;
using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Api;

public static class EvolutionMindEndpoints
{
    public static RouteGroupBuilder MapEvolutionMindEndpoints(
        this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet("/mind", async (
            string? projectId,
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await ledger.GetSnapshotAsync(cancellationToken);
            var simulations = snapshot.Accounts
                .Single(account => account.Account == LedgerAccount.Simulation)
                .Items
                .Where(item =>
                    string.IsNullOrWhiteSpace(projectId) ||
                    string.Equals(item.ProjectId, projectId, StringComparison.Ordinal))
                .ToArray();
            var candidates = snapshot.Accounts
                .Single(account => account.Account == LedgerAccount.Action)
                .Items
                .Where(item =>
                    string.IsNullOrWhiteSpace(projectId) ||
                    string.Equals(item.ProjectId, projectId, StringComparison.Ordinal))
                .ToArray();
            return Results.Ok(new
            {
                snapshot.GeneratedAt,
                snapshot.IsPaused,
                snapshot.Affect,
                snapshot.Drives,
                simulations,
                candidates,
                classification = "functional cognitive simulation; not a consciousness claim"
            });
        });
        api.MapPost("/mind/cycles", (
            CognitiveCycleRequest request,
            CognitiveMind mind,
            CancellationToken cancellationToken) =>
            mind.RunCycleAsync(request, cancellationToken));
        api.MapPost("/mind/stimuli", (
            AffectStimulusRequest request,
            CognitiveMind mind,
            CancellationToken cancellationToken) =>
            mind.ApplyStimulusAsync(request, cancellationToken));
        api.MapPost("/perception/prompts", async (
            PromptInput request,
            IPromptReceiver receiver,
            SensorRunner runner,
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await ledger.GetSnapshotAsync(cancellationToken);

            if (snapshot.IsPaused)
            {
                throw new InvalidOperationException(
                    "Prompt intake is blocked while the governance kernel is paused.");
            }

            await receiver.EnqueueAsync(request, cancellationToken);
            return await runner.RunAsync(receiver.SensorId, cancellationToken);
        });
        api.MapGet("/projects", (ProjectRegistry projects) =>
            Results.Ok(projects.List()));
        api.MapPost("/candidates/{candidateId}/approve", (
            string candidateId,
            CandidateApprovalRequest request,
            CognitiveLedgerService ledger,
            CancellationToken cancellationToken) =>
            ledger.ApproveCandidateAsync(candidateId, request, cancellationToken));

        return api;
    }
}
