using CodeMeridian.Evolution.Application.Sensors;

namespace CodeMeridian.Evolution.Infrastructure.Sensors;

public sealed class LifecycleSensor(TimeProvider timeProvider) : ISensor
{
    public string Id => "lifecycle";

    public string DisplayName => "Internal lifecycle";

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
        IReadOnlyList<SensorObservation> observations =
        [
            new(
                $"lifecycle:heartbeat:{bucket}",
                "heartbeat",
                "Meridian Evolution worker heartbeat.",
                "information",
                observedAt,
                1m)
        ];

        return Task.FromResult(observations);
    }
}
