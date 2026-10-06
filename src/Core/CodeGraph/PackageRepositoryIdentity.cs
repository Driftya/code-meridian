namespace CodeMeridian.Core.CodeGraph;

public static class PackageRepositoryIdentity
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var result = value.Trim().TrimEnd('/');
        if (Uri.TryCreate(result, UriKind.Absolute, out var uri) && uri.Host.Length > 0)
        {
            var builder = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" };
            result = builder.Uri.AbsoluteUri.TrimEnd('/');
        }
        return result.EndsWith(".git", StringComparison.Ordinal) ? result[..^4] : result;
    }
}
