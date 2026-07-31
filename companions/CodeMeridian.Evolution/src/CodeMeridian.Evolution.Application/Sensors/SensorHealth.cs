namespace CodeMeridian.Evolution.Application.Sensors;

public sealed record SensorHealth(
    bool IsHealthy,
    string Status,
    DateTimeOffset CheckedAt);
