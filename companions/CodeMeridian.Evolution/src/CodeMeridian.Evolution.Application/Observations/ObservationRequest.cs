namespace CodeMeridian.Evolution.Application.Observations;

public sealed record ObservationRequest(
    string Id,
    string SensorId,
    string Type,
    string Summary,
    string Severity,
    DateTimeOffset ObservedAt,
    decimal Confidence,
    string IdempotencyKey)
{
    public string ProjectId { get; init; } = "meridian-evolution";

    public string TrustLevel { get; init; } = "untrusted";

    public string? SourceUri { get; init; }
}
