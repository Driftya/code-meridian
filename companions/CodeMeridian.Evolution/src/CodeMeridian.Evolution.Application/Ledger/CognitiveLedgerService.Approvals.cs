using CodeMeridian.Evolution.Application.Journal;
using CodeMeridian.Evolution.Application.Projects;
using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Application.Ledger;

public sealed partial class CognitiveLedgerService
{
    public async Task<JournalAppendResult> ApproveCandidateAsync(
        string candidateId,
        CandidateApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        if (snapshot.IsPaused)
        {
            throw new InvalidOperationException(
                "Candidate approval is blocked while the governance kernel is paused.");
        }

        var subjectId = candidateId.StartsWith("candidate:", StringComparison.Ordinal)
            ? candidateId
            : $"candidate:{candidateId}";
        var entries = await journalStore
            .ReadAllAsync(cancellationToken)
            .ConfigureAwait(false);
        var source = entries
            .OrderByDescending(entry => entry.Sequence)
            .SelectMany(entry => entry.Transaction.Postings
                .Where(posting =>
                    posting.Account == LedgerAccount.Action &&
                    string.Equals(posting.SubjectId, subjectId, StringComparison.Ordinal))
                .Select(posting => new { Entry = entry, Posting = posting }))
            .FirstOrDefault()
            ?? throw new KeyNotFoundException($"Candidate '{candidateId}' was not found.");

        if (source.Posting.Reconciliation != ReconciliationState.Pending)
        {
            throw new InvalidOperationException(
                $"Candidate '{candidateId}' is not awaiting approval.");
        }

        var occurredAt = timeProvider.GetUtcNow();
        var evidence = new EvidenceReference(
            $"approval:{request.IdempotencyKey}",
            "human-approval",
            request.Reason,
            occurredAt,
            1m);

        return await AppendAsync(
            JournalEventKind.ApprovalRecorded,
            request.Actor,
            $"Approved candidate {subjectId} for isolated change preparation.",
            [evidence],
            [
                source.Posting with
                {
                    Provenance = evidence.Id,
                    Confidence = 1m,
                    Reconciliation = ReconciliationState.Reconciled
                },
                new LedgerPosting(
                    LedgerAccount.Authority,
                    $"approval:{subjectId}",
                    request.Reason,
                    evidence.Id,
                    1m,
                    ReconciliationState.Reconciled)
                {
                    ProjectId = source.Posting.ProjectId
                }
            ],
            request.IdempotencyKey,
            occurredAt,
            causalParentId: source.Entry.Transaction.Id.ToString("D"),
            authorityReference: evidence.Id,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
