namespace CodeMeridian.Core.CodeGraph;

public sealed record ConfigurationUsage
{
    public required CodeNode ConsumerNode { get; init; }
    public required CodeNode KeyNode { get; init; }
    public required string RelationshipType { get; init; }
    public string? RawKey { get; init; }
    public string? AccessPattern { get; init; }
    public string? OptionsType { get; init; }
    public double? Confidence { get; init; }
}
