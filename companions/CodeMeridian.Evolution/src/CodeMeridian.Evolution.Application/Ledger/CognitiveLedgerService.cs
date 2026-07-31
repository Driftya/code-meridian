using CodeMeridian.Evolution.Application.Cognition;
using CodeMeridian.Evolution.Application.Journal;
using CodeMeridian.Evolution.Application.Projections;
using CodeMeridian.Evolution.Domain.Cognition;
using CodeMeridian.Evolution.Domain.Governance;
using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Application.Ledger;

public sealed partial class CognitiveLedgerService
{
    private readonly IJournalStore journalStore;
    private readonly TimeProvider timeProvider;

    public CognitiveLedgerService(
        IJournalStore journalStore,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(journalStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.journalStore = journalStore;
        this.timeProvider = timeProvider;
    }

    public async Task<JournalAppendResult> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var entries = await journalStore.ReadAllAsync(cancellationToken).ConfigureAwait(false);

        if (entries.Count > 0)
        {
            return new JournalAppendResult(entries[0], WasAppended: false);
        }

        var occurredAt = timeProvider.GetUtcNow();
        var evidence = new EvidenceReference(
            "constitution:1.1.0",
            "authorized-declaration",
            "Meridian Evolution constitution version 1.1.0.",
            occurredAt,
            1m);
        var affect = AffectState.Baseline(occurredAt);
        var postings = new List<LedgerPosting>
        {
            new(
                LedgerAccount.Identity,
                "agent:meridian-evolution",
                "Meridian Evolution — a separate persistent cognitive simulation.",
                evidence.Id,
                1m,
                ReconciliationState.Reconciled),
            new(
                LedgerAccount.Authority,
                "governance:autonomy",
                AutonomyLevel.Recommend.ToString(),
                evidence.Id,
                1m,
                ReconciliationState.Reconciled),
            new(
                LedgerAccount.System,
                "governance:state",
                "running",
                evidence.Id,
                1m,
                ReconciliationState.Reconciled),
            new(
                LedgerAccount.Project,
                "meridian-evolution",
                "The mind's own runtime and source repository.",
                evidence.Id,
                1m,
                ReconciliationState.Reconciled),
            new LedgerPosting(
                LedgerAccount.Project,
                "codemeridian",
                "A separate observed project and optional code-intelligence instrument.",
                evidence.Id,
                1m,
                ReconciliationState.Reconciled)
            {
                ProjectId = "codemeridian"
            }
        };
        postings.AddRange(CognitiveStatePostingFactory.Create(
            affect,
            CognitiveHomeostasis.DeriveDrives(affect),
            evidence.Id));

        return await AppendAsync(
            JournalEventKind.Initialized,
            "system:bootstrap",
            "Initialize the standalone cognitive ledger.",
            [evidence],
            postings,
            "system:initialize:v1",
            occurredAt,
            uncertainty: 0m,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<CognitiveSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var entries = await journalStore.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return CognitiveProjectionService.Rebuild(entries, timeProvider.GetUtcNow());
    }

    public Task<IReadOnlyList<JournalEntry>> GetJournalAsync(
        CancellationToken cancellationToken = default)
    {
        return journalStore.ReadAllAsync(cancellationToken);
    }

    public async Task<TrialBalanceReport> GetTrialBalanceAsync(
        CancellationToken cancellationToken = default)
    {
        var entries = await journalStore.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return TrialBalanceService.Evaluate(entries);
    }

    private Task<JournalAppendResult> AppendAsync(
        JournalEventKind kind,
        string actor,
        string summary,
        IEnumerable<EvidenceReference> evidence,
        IEnumerable<LedgerPosting> postings,
        string idempotencyKey,
        DateTimeOffset occurredAt,
        string? causalParentId = null,
        string? authorityReference = null,
        string? correctsEntryId = null,
        decimal uncertainty = 0m,
        CancellationToken cancellationToken = default)
    {
        var transaction = CognitiveTransaction.Create(
            occurredAt,
            actor,
            kind,
            summary,
            evidence,
            postings,
            causalParentId,
            authorityReference,
            correctsEntryId,
            idempotencyKey,
            uncertainty);

        return journalStore.AppendAsync(
            transaction,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}
