using CodeMeridian.Evolution.Application.Projections;
using CodeMeridian.Evolution.Domain.Cognition;
using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Application.Cognition;

public static class AttentionCompiler
{
    public static AttentionFrame Compile(
        CognitiveSnapshot snapshot,
        long afterSequence,
        string? projectId,
        int maximumItems)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (maximumItems is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumItems),
                maximumItems,
                "Attention frames support between 1 and 32 items.");
        }

        var targetProject = string.IsNullOrWhiteSpace(projectId)
            ? "meridian-evolution"
            : projectId;
        var curiosity = Drive(snapshot, DriveKind.Curiosity);
        var coherence = Drive(snapshot, DriveKind.Coherence);
        var safety = Drive(snapshot, DriveKind.Safety);
        var selections = snapshot.Unresolved
            .Where(item =>
                item.Sequence > afterSequence &&
                string.Equals(item.ProjectId, targetProject, StringComparison.Ordinal))
            .Where(item => item.Account is not LedgerAccount.Affect and not LedgerAccount.Drive)
            .Select(item =>
            {
                var score = Score(item, curiosity, coherence, safety);
                return new AttentionSelection(item, score, Explain(item, score));
            })
            .OrderByDescending(selection => selection.Score)
            .ThenByDescending(selection => selection.Item.Sequence)
            .Take(maximumItems)
            .ToArray();

        return new AttentionFrame(
            Guid.NewGuid(),
            snapshot.GeneratedAt,
            targetProject,
            Array.AsReadOnly(selections));
    }

    private static decimal Score(
        LedgerItemView item,
        decimal curiosity,
        decimal coherence,
        decimal safety)
    {
        var uncertainty = 1m - item.Confidence;
        var accountWeight = item.Account switch
        {
            LedgerAccount.Goal => 0.9m,
            LedgerAccount.Attention => 0.85m + (safety * 0.15m),
            LedgerAccount.Belief => 0.65m + (coherence * 0.2m),
            LedgerAccount.Research => 0.55m + (curiosity * 0.25m),
            LedgerAccount.Memory => 0.45m + (curiosity * 0.2m),
            LedgerAccount.Action => 0.7m,
            _ => 0.35m
        };
        var disputeWeight = item.Reconciliation == ReconciliationState.Disputed ? 0.25m : 0m;
        return Math.Clamp(accountWeight + (uncertainty * 0.25m) + disputeWeight, 0m, 1.5m);
    }

    private static decimal Drive(CognitiveSnapshot snapshot, DriveKind kind)
    {
        return snapshot.Drives.FirstOrDefault(drive => drive.Kind == kind)?.Activation ?? 0m;
    }

    private static string Explain(LedgerItemView item, decimal score)
    {
        var reason = item.Reconciliation == ReconciliationState.Disputed
            ? "disputed evidence requires reconciliation"
            : item.Account switch
            {
                LedgerAccount.Goal => "authorized goal is active",
                LedgerAccount.Attention => "safety-relevant observation requested attention",
                LedgerAccount.Belief => "belief remains uncertain",
                LedgerAccount.Research => "curiosity can reduce uncertainty",
                LedgerAccount.Action => "proposed action awaits review",
                _ => "unresolved recent evidence"
            };
        return $"{reason}; deterministic score {score:F2}";
    }
}
