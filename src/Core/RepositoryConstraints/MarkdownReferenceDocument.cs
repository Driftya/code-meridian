namespace CodeMeridian.Core.RepositoryConstraints;

public sealed record MarkdownReferenceDocument(
    string Project,
    string FileId,
    string SourcePath,
    long Generation,
    string TextFingerprint,
    string ParserFingerprint,
    string Status,
    IReadOnlyList<MarkdownReferenceFact> References,
    IReadOnlyList<RepositoryConstraintDiagnostic> Diagnostics)
{
    public string ContractVersion { get; init; } = "1.0";
}
