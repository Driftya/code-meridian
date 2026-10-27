using CodeMeridian.Core.Projects;
using CodeMeridian.Core.RepositoryConstraints;
using CodeMeridian.DocumentIndexer.Pipeline;

namespace CodeMeridian.Indexer.Cli.Commands;

internal static class MarkdownEvidenceBuilder
{
    public static async Task<MarkdownReferenceEvidence> BuildAsync(ProjectProfileSnapshot snapshot,
        DirectoryInfo root, AnalyzedSourceSnapshot source, CancellationToken cancellationToken)
    {
        var documents = new List<MarkdownReferenceDocument>();
        var references = 0;
        long characters = 0;
        var complete = true;
        foreach (var file in snapshot.Files.Where(file => file.Kind == "markdown").OrderBy(file => file.Path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (documents.Count == MarkdownReferenceEvidence.MaximumDocuments) { complete = false; break; }
            var content = await source.ReadStructuralSourceAsync(new(Path.Combine(root.FullName, file.Path)), cancellationToken);
            var document = MarkdownReferenceParser.Parse(snapshot.Profile.Project, file.Path, content, snapshot.Generation, cancellationToken);
            references += document.References.Count;
            characters += document.References.Sum(fact => (long)fact.RawTarget.Length + fact.Target.Length);
            if (references > MarkdownReferenceEvidence.MaximumReferences || characters > MarkdownReferenceEvidence.MaximumTargetCharacters)
            {
                complete = false;
                break;
            }
            documents.Add(document);
            complete &= document.Status == "complete";
        }
        return new(source.Fingerprint, MarkdownReferenceParser.ParserFingerprint, complete ? "complete" : "incomplete", documents);
    }
}
