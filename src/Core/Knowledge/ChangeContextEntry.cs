namespace CodeMeridian.Core.Knowledge;

public sealed record ChangeContextEntry
{
    public const string MetadataKind = "human-cognitive-seed";

    public required string Id { get; init; }
    public required string NodeId { get; init; }
    public required string Statement { get; init; }
    public required string ContextKind { get; init; }
    public required string Provenance { get; init; }
    public required bool UserConfirmed { get; init; }
    public required string ProjectContext { get; init; }
    public required string ContentHash { get; init; }
    public string? TargetSourceHashAtWrite { get; init; }
    public DateTimeOffset? TargetUpdatedAtAtWrite { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
