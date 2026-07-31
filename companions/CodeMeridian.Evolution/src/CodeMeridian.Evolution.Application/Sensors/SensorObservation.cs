namespace CodeMeridian.Evolution.Application.Sensors;

public sealed record SensorObservation(
    string Id,
    string Type,
    string Summary,
    string Severity,
    DateTimeOffset ObservedAt,
    decimal Confidence)
{
    public string ProjectId { get; init; } = "meridian-evolution";

    public string TrustLevel { get; init; } = "untrusted";

    public string? SourceUri { get; init; }
}
