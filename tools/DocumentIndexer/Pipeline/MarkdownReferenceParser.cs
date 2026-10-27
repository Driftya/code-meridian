using System.Security.Cryptography;
using System.Text;
using CodeMeridian.Core.RepositoryConstraints;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace CodeMeridian.DocumentIndexer.Pipeline;

// Structural evidence only. The legacy searchable-reference extractor keeps its existing behavior.
public static class MarkdownReferenceParser
{
    private const int MaximumCharacters = 1_000_000;
    private const int MaximumReferences = 1000;
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder { MaximumNestingDepth = 128 }.UsePreciseSourceLocation().Build();
    private static readonly Lazy<string> Fingerprint = new(() =>
    {
        var hashes = new[] { typeof(Markdown).Assembly, typeof(MarkdownReferenceParser).Assembly, typeof(MarkdownReferenceTarget).Assembly }
            .Select(assembly =>
            {
                using var stream = File.OpenRead(assembly.Location);
                return Convert.ToHexString(SHA256.HashData(stream));
            });
        return Hash("markdown-references-v1;commonmark;precise;depth128;no-extensions;" + string.Join(';', hashes));
    });

    public static string ParserFingerprint => Fingerprint.Value;

    public static MarkdownReferenceDocument Parse(string project, string sourcePath, string content, long generation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(project) || project.Length > 200 || project != project.Trim() || project.Any(char.IsControl))
            throw new ArgumentException("A bounded project identity is required.", nameof(project));
        if (generation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
        sourcePath = MarkdownReferenceTarget.Normalize("", sourcePath).Path!;
        var references = new List<MarkdownReferenceFact>();
        var diagnostics = new List<RepositoryConstraintDiagnostic>();
        var lineStarts = new List<int> { 0 };
        void Diagnostic(string code, string reason)
        {
            if (diagnostics.Count < 8) diagnostics.Add(new(sourcePath, code, reason));
        }
        var fingerprint = Fingerprint.Value;
        if (content.Length > MaximumCharacters)
            Diagnostic("document_limit", "Structural parsing accepts at most 1000000 UTF-16 code units per file.");
        else
        {
            try
            {
                for (var index = 0; index < content.Length; index++)
                {
                    if (content[index] == '\r') { if (index + 1 < content.Length && content[index + 1] == '\n') index++; lineStarts.Add(index + 1); }
                    else if (content[index] == '\n') lineStarts.Add(index + 1);
                }
                var document = Markdown.Parse(content, Pipeline);
                foreach (var node in document.Descendants())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (node is HtmlBlock or HtmlInline)
                        Diagnostic("unsupported_html", "Raw HTML is not interpreted as local link/image evidence.");
                    if (node is not (LinkInline or AutolinkInline)) continue;
                    if (references.Count == MaximumReferences)
                    {
                        Diagnostic("reference_limit", "Only the first 1000 references are retained; structural coverage is incomplete.");
                        break;
                    }
                    var targetSpan = node is LinkInline link ? link.UrlSpan : new SourceSpan(node.Span.Start + 1, node.Span.End - 1);
                    if (!ValidSpan(node.Span, content.Length) || !ValidSpan(targetSpan, content.Length))
                    {
                        Diagnostic("unsupported_span", "The parser did not expose source-backed reference and target spans.");
                        continue;
                    }
                    var target = node is LinkInline inline ? inline.Url ?? "" : ((AutolinkInline)node).Url;
                    if (node is AutolinkInline { IsEmail: true }) target = "mailto:" + target;
                    if (targetSpan.Length > 2048 || target.Length > 2048)
                    {
                        Diagnostic("target_limit", "Reference targets are limited to 2048 UTF-16 code units.");
                        continue;
                    }
                    var normalized = MarkdownReferenceTarget.Normalize(target, sourcePath);
                    references.Add(new(node is LinkInline { IsImage: true } ? "image" : "link", Span(node.Span), Span(targetSpan),
                        content.Substring(targetSpan.Start, targetSpan.Length), target,
                        normalized.Kind, normalized.Path, normalized.Fragment));
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                references.Clear();
                Diagnostic("parse_failed", "The Markdown parser could not complete within its configured syntax limits.");
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(project, $"{project}::doc::{sourcePath}", sourcePath, generation, Hash(content), fingerprint,
            diagnostics.Count == 0 ? "complete" : "incomplete", references, diagnostics);

        MarkdownReferenceSpan Span(SourceSpan span)
        {
            var line = lineStarts.BinarySearch(span.Start);
            if (line < 0) line = ~line - 1;
            return new(span.Start, span.Length, line + 1, span.Start - lineStarts[line] + 1);
        }
    }

    private static bool ValidSpan(SourceSpan span, int length) => span.Start >= 0 && span.Length >= 0
        && span.Start <= length && span.End < length;

    private static string Hash(string value) => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
