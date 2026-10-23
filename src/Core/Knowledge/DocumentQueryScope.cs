using CodeMeridian.Core.Projects;

namespace CodeMeridian.Core.Knowledge;

/// <summary>Ordinal, repository-relative inventory identities; roots include descendants, targets select one file.</summary>
public sealed record DocumentQueryScope
{
    public DocumentQueryScope(string? rootPath = null, string? targetPath = null)
    {
        RootPath = rootPath is null or "." ? "." : ProjectProfilePath.Normalize(rootPath);
        TargetPath = targetPath is null ? null : ProjectProfilePath.Normalize(targetPath);
        if (TargetPath is not null && RootPath != "." && !TargetPath.StartsWith(RootPath + "/", StringComparison.Ordinal))
            throw new ArgumentException("The document target must be a file beneath the requested root.", nameof(targetPath));
    }

    public string RootPath { get; }
    public string? TargetPath { get; }
    public string EvidencePath => TargetPath ?? RootPath;

    public bool Contains(string? source)
    {
        if (source is null) return false;
        string path;
        try { path = ProjectProfilePath.Normalize(source); }
        catch (ArgumentException) { return false; }
        return TargetPath is not null ? path == TargetPath
            : RootPath == "." || path.StartsWith(RootPath + "/", StringComparison.Ordinal);
    }

    public bool IsCoveredBy(string scope) => scope == "." || EvidencePath == scope
        || EvidencePath.StartsWith(scope + "/", StringComparison.Ordinal);

    public bool Overlaps(string scope) => IsCoveredBy(scope)
        || TargetPath is null && (RootPath == "." || scope.StartsWith(RootPath + "/", StringComparison.Ordinal));
}
