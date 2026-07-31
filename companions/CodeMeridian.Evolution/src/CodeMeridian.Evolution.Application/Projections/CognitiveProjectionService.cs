using System.Globalization;
using CodeMeridian.Evolution.Domain.Cognition;
using CodeMeridian.Evolution.Domain.Governance;
using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Application.Projections;

public static class CognitiveProjectionService
{
    public static CognitiveSnapshot Rebuild(
        IReadOnlyList<JournalEntry> entries,
        DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var trialBalance = TrialBalanceService.Evaluate(entries);
        var latestItems = entries
            .OrderBy(entry => entry.Sequence)
            .SelectMany(entry => entry.Transaction.Postings.Select(posting =>
                new LedgerItemView(
                    entry.Sequence,
                    entry.Transaction.Id,
                    entry.Transaction.Kind,
                    entry.Transaction.OccurredAt,
                    posting.Account,
                    posting.SubjectId,
                    posting.Summary,
                    posting.Provenance,
                    posting.Confidence,
                    posting.Reconciliation)
                {
                    ProjectId = posting.ProjectId
                }))
            .GroupBy(item => (item.Account, item.SubjectId))
            .Select(group => group.MaxBy(item => item.Sequence)!)
            .OrderByDescending(item => item.Sequence)
            .ToArray();

        var governanceState = latestItems.FirstOrDefault(item =>
            item.Account == LedgerAccount.System &&
            string.Equals(item.SubjectId, "governance:state", StringComparison.Ordinal));
        var autonomyPosting = latestItems.FirstOrDefault(item =>
            item.Account == LedgerAccount.Authority &&
            string.Equals(item.SubjectId, "governance:autonomy", StringComparison.Ordinal));
        var autonomyLevel = autonomyPosting is not null &&
                            Enum.TryParse<AutonomyLevel>(
                                autonomyPosting.Summary,
                                ignoreCase: true,
                                out var parsedLevel)
            ? parsedLevel
            : AutonomyLevel.Recommend;

        var accounts = Enum.GetValues<LedgerAccount>()
            .Select(account => new LedgerAccountView(
                account,
                latestItems.Where(item => item.Account == account).ToArray()))
            .ToArray();
        var affect = ProjectAffect(latestItems, generatedAt);

        return new CognitiveSnapshot(
            generatedAt,
            string.Equals(governanceState?.Summary, "paused", StringComparison.OrdinalIgnoreCase),
            autonomyLevel,
            trialBalance.EntryCount,
            trialBalance.HeadHash,
            trialBalance.IsBalanced,
            trialBalance.Violations.ToArray(),
            accounts,
            latestItems
                .Where(item =>
                    item.Account == LedgerAccount.Goal &&
                    item.Reconciliation == ReconciliationState.Pending)
                .ToArray(),
            latestItems
                .Where(item =>
                    item.Account == LedgerAccount.Attention &&
                    item.Reconciliation != ReconciliationState.Reconciled)
                .ToArray(),
            latestItems
                .Where(item => item.Reconciliation != ReconciliationState.Reconciled)
                .ToArray(),
            affect,
            CognitiveHomeostasis.DeriveDrives(affect));
    }

    private static AffectState ProjectAffect(
        IReadOnlyList<LedgerItemView> latestItems,
        DateTimeOffset generatedAt)
    {
        var affectItems = latestItems
            .Where(item => item.Account == LedgerAccount.Affect)
            .ToArray();

        if (affectItems.Length == 0)
        {
            return AffectState.Baseline(generatedAt);
        }

        var baseline = AffectState.Baseline(affectItems.Max(item => item.OccurredAt));
        var state = new AffectState(
            ReadAffectValue(affectItems, "valence", baseline.Valence),
            ReadAffectValue(affectItems, "arousal", baseline.Arousal),
            ReadAffectValue(affectItems, "dopamine", baseline.Dopamine),
            ReadAffectValue(affectItems, "curiosity", baseline.Curiosity),
            ReadAffectValue(affectItems, "fatigue", baseline.Fatigue),
            ReadAffectValue(affectItems, "frustration", baseline.Frustration),
            affectItems.Max(item => item.OccurredAt));
        return CognitiveHomeostasis.Decay(state, generatedAt);
    }

    private static decimal ReadAffectValue(
        IEnumerable<LedgerItemView> items,
        string subjectId,
        decimal fallback)
    {
        var item = items.FirstOrDefault(candidate =>
            string.Equals(candidate.SubjectId, subjectId, StringComparison.Ordinal));

        return item is not null &&
               decimal.TryParse(
                   item.Summary,
                   NumberStyles.Number,
                   CultureInfo.InvariantCulture,
                   out var value)
            ? value
            : fallback;
    }
}
