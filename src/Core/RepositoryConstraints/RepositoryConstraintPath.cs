using System.Text;
using System.Text.RegularExpressions;

namespace CodeMeridian.Core.RepositoryConstraints;

public static class RepositoryConstraintPath
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 || path.Any(char.IsControl))
            throw new ArgumentException("A bounded repository-relative path is required.");
        path = path.Replace('\\', '/');
        if (path.StartsWith('/') || path.Contains(':'))
            throw new ArgumentException("Absolute paths are not allowed.");
        var segments = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) throw new ArgumentException("Paths must stay inside the repository.");
                segments.RemoveAt(segments.Count - 1);
            }
            else segments.Add(segment);
        }
        if (segments.Count == 0) throw new ArgumentException("A file or directory path is required.");
        return string.Join('/', segments);
    }

    internal static bool ValidPattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > 2048 || pattern.Any(char.IsControl)) return false;
        pattern = pattern.Replace('\\', '/');
        if (pattern.StartsWith('/') || pattern.IndexOfAny([':', '[', ']', '{', '}', '!']) >= 0) return false;
        return pattern.Split('/').All(segment => segment is not ("" or "." or "..")
            && (!segment.Contains("**", StringComparison.Ordinal) || segment == "**"));
    }

    internal static Regex Matcher(string pattern, bool caseSensitive)
    {
        var segments = pattern.Replace('\\', '/').Split('/');
        var expression = new StringBuilder("\\A");
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment == "**") expression.Append(index == segments.Length - 1 ? ".*" : "(?:[^/]+/)*");
            else
            {
                foreach (var character in segment)
                    expression.Append(character switch { '*' => "[^/]*", '?' => "[^/]", _ => Regex.Escape(character.ToString()) });
                if (index < segments.Length - 1) expression.Append('/');
            }
        }
        expression.Append("\\z");
        return new Regex(expression.ToString(), RegexOptions.CultureInvariant | RegexOptions.NonBacktracking
            | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase));
    }
}
