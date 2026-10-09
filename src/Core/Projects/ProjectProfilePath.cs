namespace CodeMeridian.Core.Projects;

public static class ProjectProfilePath
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A repository-relative file path is required.", nameof(path));
        var normalized = path.Replace('\\', '/');
        if (normalized.Length > 2048 || normalized.StartsWith('/') || normalized.Contains(':')
            || normalized.Any(char.IsControl)
            || normalized.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new ArgumentException("File paths must be normalized repository-relative paths without traversal.", nameof(path));
        return normalized;
    }
}
