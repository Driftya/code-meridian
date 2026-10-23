namespace CodeMeridian.Core.RepositoryConstraints;

// Offsets and length count UTF-16 code units; line and column are one-based.
public sealed record MarkdownReferenceSpan(int Offset, int Length, int Line, int Column);
