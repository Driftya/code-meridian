namespace CodeMeridian.Core.KeywordGraph;

public sealed record KeywordSourceNode
{
    public required string Id { get; init; }
    public string? ProjectContext { get; init; }
    public required string Kind { get; init; }
    public string? ExistingChecksum { get; init; }
    public Dictionary<string, string?> TextBySource { get; init; } = [];
}

public sealed record ExtractedKeyword
{
    public required string Value { get; init; }
    public required string NormalizedValue { get; init; }
    public required int Count { get; init; }
    public required double Weight { get; init; }
    public IReadOnlyList<string> Sources { get; init; } = [];
}

public sealed record ReplaceKeywordRelationshipsCommand
{
    public required string SourceNodeId { get; init; }
    public required string KeywordTextChecksum { get; init; }
    public required int EnrichmentVersion { get; init; }
    public required IReadOnlyList<ExtractedKeyword> Keywords { get; init; }
}

public sealed record KeywordRelatedNode
{
    public required string TargetNodeId { get; init; }
    public required string TargetKind { get; init; }
    public required int SharedKeywordCount { get; init; }
    public required double Score { get; init; }
    public IReadOnlyList<string> MatchedKeywords { get; init; } = [];
}

public sealed record KeywordSourceNodeQuery
{
    public string? ProjectContext { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; }
}

public sealed record KeywordRelatedNodeQuery
{
    public required string SourceNodeId { get; init; }
    public IReadOnlyList<string> TargetKinds { get; init; } = [];
    public required int MinimumSharedKeywords { get; init; }
    public required double MinimumScore { get; init; }
    public required double MaximumDocumentFrequencyRatio { get; init; }
    public required int Limit { get; init; }
}
