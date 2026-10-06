namespace CodeMeridian.Core.CodeGraph;

public sealed record PackageReferenceResolution(
    string Status, string Reason, string? TargetId = null, string? ProducerProject = null,
    string? ProducerVersion = null, IReadOnlyList<string>? Candidates = null);
