namespace CodeMeridian.Sdk;

public sealed record CodeNodeIngestRequest(
    string Id,
    string Name,
    string Type,
    string? Namespace = null,
    string? FilePath = null,
    int? LineNumber = null,
    int? LineCount = null,
    string? Summary = null,
    string? SourceSnippet = null,
    string? SourceHash = null,
    string? FileRole = null,
    string? ProjectContext = null,
    Dictionary<string, string>? Properties = null,
    string? EmbeddingCsv = null);
