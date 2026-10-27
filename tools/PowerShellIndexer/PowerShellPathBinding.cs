using CodeMeridian.Core.RepositoryConstraints;

namespace CodeMeridian.PowerShellIndexer;

internal static class PowerShellPathBinding
{
    internal static string? Resolve(string source, string kind, string? value)
    {
        if (kind != "script_root" || string.IsNullOrEmpty(value) || value.Length > 2048 || value.IndexOfAny(['$', '*', '?', '[', ']', '\\']) >= 0)
            return null;
        try
        {
            var directory = source.Contains('/') ? source[..source.LastIndexOf('/')] + "/" : "";
            return RepositoryConstraintPath.Normalize(directory + value);
        }
        catch (ArgumentException) { return null; }
    }

    internal static string? ManifestRoot(string source, string value)
    {
        if (value.StartsWith('/') || value.Contains(':')) return null;
        return Resolve(source, "script_root", value);
    }
}
