namespace CodeMeridian.Core.CodeGraph;

public sealed record CodeEdge
{
    public string? Id { get; init; }
    public required string SourceId { get; init; }
    public required string TargetId { get; init; }
    public required CodeEdgeType Type { get; init; }

    /// <summary>Whether the call-site uses await/async.</summary>
    public bool? IsAsync { get; init; }

    /// <summary>Source location of the call, e.g. "src/Services/UserService.cs:42".</summary>
    public string? CallSite { get; init; }

    /// <summary>Number of arguments at the call-site.</summary>
    public int? ParamCount { get; init; }

    /// <summary>Indexer confidence score (0–1). Lower values indicate inferred/heuristic edges.</summary>
    public double? Confidence { get; init; }

    public EdgeEvidenceKind? EvidenceKind { get; init; }
    public string? EvidenceReason { get; init; }
    public string? Resolver { get; init; }
    public string? SourceFilePath { get; init; }
    public int? SourceLine { get; init; }
    public int? SourceColumn { get; init; }
    public int? SourceEndLine { get; init; }
    public int? SourceEndColumn { get; init; }
    public Dictionary<string, string>? EvidenceDetails { get; init; }

    /// <summary>Additional edge metadata for specialized graph relationships.</summary>
    public Dictionary<string, string> Properties { get; init; } = [];

    public string? ValidateEvidence()
    {
        if (EvidenceKind is { } kind && !Enum.IsDefined(kind))
            return "Invalid evidence kind.";
        if (!IsCode(EvidenceReason) || !IsCode(Resolver))
            return "Evidence reason and resolver must be at most 64 characters and contain only letters, digits, dots, underscores, or hyphens.";
        if (SourceFilePath is { Length: > 1024 } || SourceFilePath?.Any(char.IsControl) == true)
            return "Source file path must be at most 1024 characters without control characters.";
        if (!ValidPosition(SourceLine, 10_000_000) || !ValidPosition(SourceEndLine, 10_000_000)
            || !ValidPosition(SourceColumn, 100_000) || !ValidPosition(SourceEndColumn, 100_000))
            return "Source lines and columns must be positive and within the documented bounds.";
        if (SourceColumn is not null && SourceLine is null || SourceEndColumn is not null && SourceEndLine is null
            || SourceEndLine is not null && SourceLine is null
            || SourceEndLine < SourceLine
            || SourceEndLine == SourceLine && SourceEndColumn is not null && SourceColumn is not null && SourceEndColumn < SourceColumn)
            return "Source span is incomplete or reversed.";
        if (EvidenceDetails is { Count: > 8 } || EvidenceDetails?.Any(pair =>
                pair.Key.Length is 0 or > 64 || !pair.Key.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-')
                || pair.Value is null || pair.Value.Length > 256 || pair.Value.Any(char.IsControl)) == true)
            return "Evidence details must have at most eight bounded string entries.";
        return null;
    }

    private static bool ValidPosition(int? value, int maximum) => value is null || value is > 0 && value <= maximum;
    private static bool IsCode(string? value) => value is null || value.Length is > 0 and <= 64
        && value.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-');
}

public enum CodeEdgeType
{
    Contains,
    Calls,
    Implements,
    Inherits,
    Uses,
    UsesClass,
    UsesId,
    DependsOn,
    Overrides,
    DefinesSelector,
    ImportsStyle,
    UsesCssVariable,
    DefinesCssVariable,
    Reads,
    Writes,
    PublishesTo,
    SubscribesTo,
    DefinesConfig,
    OverridesConfig,
    ReadsConfig,
    BindsConfig
}
