using System.Text.Json.Serialization;
using CodeMeridian.Core.Projects;

namespace CodeMeridian.Core.RepositoryConstraints;

// Analyzer-owned facts replaced atomically with the enclosing inventory, never with search chunks.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MarkdownReferenceEvidence(
    string SourceFingerprint,
    string ParserFingerprint,
    string Status,
    IReadOnlyList<MarkdownReferenceDocument> Documents)
{
    public const int MaximumDocuments = 10000;
    public const int MaximumReferences = 10000;
    public const int MaximumTargetCharacters = 1000000;
    public string ContractVersion { get; init; } = "1.0";

    public string? Validate(ProjectProfileSnapshot snapshot)
    {
        if (ContractVersion != "1.0" || !ProjectAnalyzerFingerprint.ValidHash(SourceFingerprint)
            || SourceFingerprint != snapshot.Profile.SourceFingerprint
            || !ProjectAnalyzerFingerprint.ValidHash(ParserFingerprint)
            || Status is not ("complete" or "incomplete") || Documents is null || Documents.Count > MaximumDocuments)
            return "Markdown evidence requires bounded versioned facts bound to the inventory source and executing parser.";
        var sources = snapshot.Files.Where(file => file.Kind == "markdown").Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var references = 0;
        long characters = 0;
        foreach (var document in Documents)
        {
            if (document is null || document.ContractVersion != "1.0" || document.Project != snapshot.Profile.Project
                || document.Generation != snapshot.Generation || !sources.Contains(document.SourcePath) || !seen.Add(document.SourcePath)
                || document.FileId != $"{document.Project}::doc::{document.SourcePath}"
                || !ProjectAnalyzerFingerprint.ValidHash(document.TextFingerprint)
                || document.ParserFingerprint != ParserFingerprint || document.Status is not ("complete" or "incomplete")
                || document.References is null || document.References.Count > 1000
                || document.Diagnostics is null || document.Diagnostics.Count > 8
                || document.Status == "complete" && document.Diagnostics.Count != 0)
                return "Markdown documents must identify unique accepted Markdown files in the same project/generation with consistent parser evidence.";
            if (Status == "complete" && document.Status != "complete")
                return "Complete Markdown evidence cannot contain incomplete documents.";
            foreach (var diagnostic in document.Diagnostics)
                if (diagnostic is null || diagnostic.Location != document.SourcePath
                    || !ProjectProfileSnapshot.ValidName(diagnostic.Code, 64) || !ProjectProfileSnapshot.ValidName(diagnostic.Reason, 512))
                    return "Markdown diagnostics require bounded file locations, codes, and reasons.";
            foreach (var fact in document.References)
            {
                if (fact is null || fact.Kind is not ("link" or "image") || fact.RawTarget is null || fact.RawTarget.Length > 2048
                    || fact.Target is null || fact.Target.Length > 2048 || !ValidSpan(fact.SourceSpan) || !ValidSpan(fact.TargetSpan)
                    || fact.TargetSpan.Length != fact.RawTarget.Length)
                    return "Markdown references require bounded targets and source-backed spans.";
                var target = MarkdownReferenceTarget.Normalize(fact.Target, document.SourcePath);
                if (fact.TargetKind != target.Kind || fact.NormalizedPath != target.Path || fact.Fragment != target.Fragment)
                    return "Markdown reference normalization must match its target.";
                references++;
                characters += fact.RawTarget.Length + fact.Target.Length;
                if (references > MaximumReferences || characters > MaximumTargetCharacters)
                    return "Markdown evidence exceeds its project reference or target-text limit.";
            }
        }
        return Status == "complete" && seen.Count != sources.Count
            ? "Complete Markdown evidence must account for every accepted Markdown file, including empty files." : null;
    }

    private static bool ValidSpan(MarkdownReferenceSpan? span) => span is { Offset: >= 0, Length: >= 0, Line: > 0, Column: > 0 }
        && (long)span.Offset + span.Length <= int.MaxValue;
}
