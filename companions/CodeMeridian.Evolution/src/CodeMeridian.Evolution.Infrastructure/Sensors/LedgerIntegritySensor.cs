using CodeMeridian.Evolution.Application.Journal;
using CodeMeridian.Evolution.Application.Sensors;
using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Infrastructure.Sensors;

public sealed class LedgerIntegritySensor(
    IJournalStore journalStore,
    TimeProvider timeProvider) : ISensor
{
    public string Id => "ledger-integrity";

    public string DisplayName => "Ledger integrity";

    public Task<SensorHealth> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SensorHealth(
            IsHealthy: true,
            "ready",
            timeProvider.GetUtcNow()));
    }

    public async Task<IReadOnlyList<SensorObservation>> CollectAsync(
        CancellationToken cancellationToken = default)
    {
        var entries = await journalStore.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var report = TrialBalanceService.Evaluate(entries);
        var observedAt = timeProvider.GetUtcNow();
        var head = string.IsNullOrEmpty(report.HeadHash) ? "empty" : report.HeadHash[..12];
        IReadOnlyList<SensorObservation> observations =
        [
            new(
                $"ledger-integrity:{report.EntryCount}:{head}",
                report.IsBalanced ? "integrity-ok" : "integrity-violation",
                report.IsBalanced
                    ? $"Journal is balanced at {report.EntryCount} entries."
                    : $"Journal has {report.Violations.Count} integrity violation(s).",
                report.IsBalanced ? "information" : "critical",
                observedAt,
                report.IsBalanced ? 1m : 0m)
        ];

        return observations;
    }
}
