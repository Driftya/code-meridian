using CodeMeridian.Evolution.Application.Ledger;
using CodeMeridian.Evolution.Application.Observations;

namespace CodeMeridian.Evolution.Application.Sensors;

public sealed class SensorRunner(
    SensorRegistry registry,
    CognitiveLedgerService ledgerService)
{
    public async Task<SensorRunResult> RunAsync(
        string sensorId,
        CancellationToken cancellationToken = default)
    {
        var sensor = registry.Get(sensorId);
        var snapshot = await ledgerService
            .GetSnapshotAsync(cancellationToken)
            .ConfigureAwait(false);

        if (snapshot.IsPaused)
        {
            return new SensorRunResult(
                sensor.Id,
                new SensorHealth(
                    IsHealthy: false,
                    "governance-paused",
                    snapshot.GeneratedAt),
                0,
                0);
        }

        var health = await sensor.CheckHealthAsync(cancellationToken).ConfigureAwait(false);

        if (!health.IsHealthy)
        {
            return new SensorRunResult(sensor.Id, health, 0, 0);
        }

        var observations = await sensor
            .CollectAsync(cancellationToken)
            .ConfigureAwait(false);
        var appendedCount = 0;

        foreach (var observation in observations)
        {
            var result = await ledgerService.RecordObservationAsync(
                new ObservationRequest(
                    observation.Id,
                    sensor.Id,
                    observation.Type,
                    observation.Summary,
                    observation.Severity,
                    observation.ObservedAt,
                    observation.Confidence,
                    $"sensor:{sensor.Id}:{observation.Id}")
                {
                    ProjectId = observation.ProjectId,
                    TrustLevel = observation.TrustLevel,
                    SourceUri = observation.SourceUri
                },
                cancellationToken).ConfigureAwait(false);

            if (result.WasAppended)
            {
                appendedCount++;
            }
        }

        return new SensorRunResult(
            sensor.Id,
            health,
            observations.Count,
            appendedCount);
    }
}
