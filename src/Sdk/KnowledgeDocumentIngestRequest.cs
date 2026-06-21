namespace CodeMeridian.Sdk;

public sealed record KnowledgeDocumentIngestRequest(
    string Content,
    string? Id = null,
    string? Source = null,
    string? ProjectContext = null,
    string? RelatedNodeIdsCsv = null,
    string? RelatedDocumentIdsCsv = null);
