using System.Text;
using System.Text.RegularExpressions;

namespace CodeMeridian.Core.RepositoryConstraints;

public sealed record MarkdownReferenceTarget(string Kind, string? Path, string? Fragment)
{
    public static MarkdownReferenceTarget Normalize(string target, string sourcePath)
    {
        sourcePath = RepositoryConstraintPath.Normalize(sourcePath);
        if (target.Length > 2048 || target.Any(char.IsControl)) return new("unsupported", null, null);
        var hash = target.IndexOf('#');
        var path = hash < 0 ? target : target[..hash];
        var fragment = hash < 0 ? null : target[(hash + 1)..];
        // Schemes are classified before decoding; an encoded colon cannot become a remote URL.
        if (path.StartsWith("//", StringComparison.Ordinal)) return new("external", null, fragment);
        var scheme = Regex.Match(path, @"\A([a-zA-Z][a-zA-Z0-9+.-]*):", RegexOptions.CultureInvariant);
        if (scheme.Success)
            return new(scheme.Groups[1].Value.ToLowerInvariant() is "http" or "https" or "mailto" ? "external" : "unsupported", null, fragment);
        var query = path.IndexOf('?');
        if (query >= 0) return new("unsupported", null, fragment);
        if (!ValidEscapes(path) || fragment is not null && !ValidEscapes(fragment)) return new("unsupported", null, fragment);
        try
        {
            path = Decode(path);
            fragment = fragment is null ? null : Decode(fragment);
        }
        catch (DecoderFallbackException) { return new("unsupported", null, fragment); }
        if (path.Any(char.IsControl) || fragment?.Any(char.IsControl) == true || path.Contains('\\') || path.Contains(':'))
            return new("unsupported", null, fragment);
        if (path.Length == 0) return new("fragment_only", sourcePath, fragment);
        // A single leading slash is explicitly repository-root-relative, never a host filesystem path.
        var rootRelative = path.StartsWith('/');
        if (path.StartsWith("//", StringComparison.Ordinal)) return new("unsupported", null, fragment);
        if (rootRelative) path = path[1..];
        else if (sourcePath.LastIndexOf('/') is var separator && separator >= 0) path = sourcePath[..(separator + 1)] + path;
        var depth = 0;
        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..") { if (--depth < 0) return new("outside_repository", null, fragment); }
            else depth++;
        }
        // Inventory describes files. Directory URLs and paths beyond the supported bound are not repository escapes.
        if (depth == 0 || path.EndsWith('/') || path.Split('/')[^1] is "." or "..") return new("unsupported", null, fragment);
        try { return new("local", RepositoryConstraintPath.Normalize(path), fragment); }
        catch (ArgumentException) { return new("unsupported", null, fragment); }
    }

    private static bool ValidEscapes(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '%') continue;
            if (index + 2 >= text.Length || !Uri.IsHexDigit(text[index + 1]) || !Uri.IsHexDigit(text[index + 2])) return false;
            index += 2;
        }
        return true;
    }

    private static string Decode(string text)
    {
        var decoded = new StringBuilder();
        var utf8 = new UTF8Encoding(false, true);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '%') { decoded.Append(text[index]); continue; }
            var bytes = new List<byte>();
            while (index + 2 < text.Length && text[index] == '%')
            {
                bytes.Add(Convert.ToByte(text.Substring(index + 1, 2), 16));
                index += 3;
            }
            decoded.Append(utf8.GetString(bytes.ToArray()));
            index--;
        }
        return decoded.ToString();
    }
}
