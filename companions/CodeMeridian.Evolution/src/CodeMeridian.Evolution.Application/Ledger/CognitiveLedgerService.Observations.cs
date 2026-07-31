using CodeMeridian.Evolution.Application.Observations;
using CodeMeridian.Evolution.Application.Journal;
using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Application.Ledger;

public sealed partial class CognitiveLedgerService
{
    public async Task<JournalAppendResult> RecordObservationAsync(
        ObservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SensorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Type);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Severity);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TrustLevel);

        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        if (snapshot.IsPaused)
        {
            throw new InvalidOperationException(
                "Observation intake is blocked while the governance kernel is paused.");
        }

        var evidence = new EvidenceReference(
            request.Id,
            request.SensorId,
            $"{request.Type} [{request.TrustLevel}]: {request.Summary}",
            request.ObservedAt,
            request.Confidence);
        var postings = new List<LedgerPosting>
        {
            new LedgerPosting(
                LedgerAccount.Memory,
                request.Id,
                request.Summary,
                request.SensorId,
                request.Confidence,
                ReconciliationState.Pending)
            {
                ProjectId = request.ProjectId
            },
            new LedgerPosting(
                LedgerAccount.Research,
                request.Id,
                request.Type,
                request.SensorId,
                request.Confidence,
                ReconciliationState.Pending)
            {
                ProjectId = request.ProjectId
            }
        };

        if (string.Equals(request.Severity, "critical", StringComparison.OrdinalIgnoreCase))
        {
            postings.Add(new LedgerPosting(
                LedgerAccount.Attention,
                request.Id,
                request.Summary,
                request.SensorId,
                request.Confidence,
                ReconciliationState.Pending)
            {
                ProjectId = request.ProjectId
            });
        }

        return await AppendAsync(
            string.Equals(request.Type, "human-prompt", StringComparison.Ordinal)
                ? JournalEventKind.PromptReceived
                : JournalEventKind.Observation,
            request.SensorId,
            request.Summary,
            [evidence],
            postings,
            request.IdempotencyKey,
            request.ObservedAt,
            uncertainty: 1m - request.Confidence,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<JournalAppendResult> ChallengeEntryAsync(
        long sequence,
        CorrectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);

        var entries = await journalStore.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var sourceEntry = entries.SingleOrDefault(entry => entry.Sequence == sequence)
            ?? throw new KeyNotFoundException($"Journal entry '{sequence}' was not found.");
        var occurredAt = timeProvider.GetUtcNow();
        var evidence = new EvidenceReference(
            $"challenge:{sequence}:{request.Actor}",
            "human-correction",
            request.Summary,
            occurredAt,
            request.Confidence);
        var postings = sourceEntry.Transaction.Postings
            .Select(posting => new LedgerPosting(
                posting.Account,
                posting.SubjectId,
                request.Summary,
                evidence.Id,
                request.Confidence,
                ReconciliationState.Disputed)
            {
                ProjectId = posting.ProjectId
            })
            .ToArray();

        return await AppendAsync(
            JournalEventKind.Adjustment,
            request.Actor,
            request.Summary,
            [evidence],
            postings,
            request.IdempotencyKey,
            occurredAt,
            causalParentId: sourceEntry.Transaction.Id.ToString("D"),
            authorityReference: $"human:{request.Actor}",
            correctsEntryId: sequence.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            uncertainty: 1m - request.Confidence,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
