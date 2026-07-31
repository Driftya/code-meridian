namespace CodeMeridian.Evolution.Application.Sensors;

public sealed record SensorRunResult(
    string SensorId,
    SensorHealth Health,
    int ObservedCount,
    int AppendedCount);
