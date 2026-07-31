using System.Diagnostics;
using CodeMeridian.Evolution.Application.Sensors;

namespace CodeMeridian.Evolution.Infrastructure.Sensors;

public sealed class SystemResourceSensor(TimeProvider timeProvider) : ISensor
{
    public string Id => "system-resource";

    public string DisplayName => "System and resources";

    public Task<SensorHealth> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SensorHealth(
            IsHealthy: true,
            "ready",
            timeProvider.GetUtcNow()));
    }

    public Task<IReadOnlyList<SensorObservation>> CollectAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var observedAt = timeProvider.GetUtcNow();
        var bucket = observedAt.ToString("yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture);

        using var process = Process.GetCurrentProcess();
        var memoryMiB = process.WorkingSet64 / 1024d / 1024d;
        IReadOnlyList<SensorObservation> observations =
        [
            new(
                $"system-resource:sample:{bucket}",
                "resource-sample",
                $"Working set {memoryMiB:F1} MiB; uptime {Environment.TickCount64 / 1000L} seconds.",
                "information",
                observedAt,
                1m)
        ];

        return Task.FromResult(observations);
    }
}
