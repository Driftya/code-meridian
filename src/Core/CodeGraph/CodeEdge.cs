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

    /// <summary>Additional edge metadata for specialized graph relationships.</summary>
    public Dictionary<string, string> Properties { get; init; } = [];
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
