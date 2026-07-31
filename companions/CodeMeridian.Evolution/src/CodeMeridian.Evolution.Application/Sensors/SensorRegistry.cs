namespace CodeMeridian.Evolution.Application.Sensors;

public sealed class SensorRegistry(IEnumerable<ISensor> sensors)
{
    private readonly IReadOnlyDictionary<string, ISensor> sensorsById = sensors
        .ToDictionary(sensor => sensor.Id, StringComparer.Ordinal);

    public IReadOnlyList<ISensor> List()
    {
        return sensorsById.Values
            .OrderBy(sensor => sensor.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public ISensor Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return sensorsById.TryGetValue(id, out var sensor)
            ? sensor
            : throw new KeyNotFoundException($"Sensor '{id}' is not registered.");
    }
}
