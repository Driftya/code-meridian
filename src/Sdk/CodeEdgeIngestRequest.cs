namespace CodeMeridian.Sdk;

public sealed record CodeEdgeIngestRequest(
    string SourceId,
    string TargetId,
    string Type,
    bool? IsAsync = null,
    string? CallSite = null,
    int? ParamCount = null,
    double? Confidence = null,
    Dictionary<string, string>? Properties = null);
