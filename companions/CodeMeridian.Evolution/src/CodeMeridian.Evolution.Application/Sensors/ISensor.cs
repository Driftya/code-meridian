namespace CodeMeridian.Evolution.Application.Sensors;

public interface ISensor
{
    string Id { get; }

    string DisplayName { get; }

    Task<SensorHealth> CheckHealthAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SensorObservation>> CollectAsync(
        CancellationToken cancellationToken = default);
}
