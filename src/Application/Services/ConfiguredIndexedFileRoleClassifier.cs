using System.Text.RegularExpressions;
using CodeMeridian.Core.CodeGraph;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Application.Services;

public sealed class ConfiguredIndexedFileRoleClassifier(IOptions<CodebaseIndexingOptions> options) : IIndexedFileRoleClassifier
{
    private readonly FileRolePatternOptions _patterns = options.Value.FileRoles ?? FileRolePatternOptions.CreateDefaults();

    public IndexedFileRole Classify(string relativePath)
    {
        var normalizedPath = Normalize(relativePath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return IndexedFileRole.Unknown;

        foreach (var role in RoleEvaluationOrder)
        {
            if (_patterns.GetPatterns(role).Any(pattern => GlobPatternMatcher.IsMatch(normalizedPath, pattern)))
                return role;
        }

        return IsLikelySourceFile(normalizedPath)
            ? IndexedFileRole.Source
            : IndexedFileRole.Unknown;
    }

    private static bool IsLikelySourceFile(string normalizedPath)
    {
        var extension = Path.GetExtension(normalizedPath);
        return extension is ".cs" or ".ts" or ".tsx" or ".js" or ".jsx";
    }

    private static string Normalize(string relativePath) =>
        relativePath.Replace('\\', '/').TrimStart('/');

    private static readonly IndexedFileRole[] RoleEvaluationOrder =
    [
        IndexedFileRole.BuildArtifact,
        IndexedFileRole.Snapshot,
        IndexedFileRole.Migration,
        IndexedFileRole.Generated,
        IndexedFileRole.Test,
        IndexedFileRole.Documentation,
        IndexedFileRole.Configuration
    ];

    private static class GlobPatternMatcher
    {
        public static bool IsMatch(string path, string pattern)
        {
            var normalizedPattern = Normalize(pattern);
            if (normalizedPattern.StartsWith("**/", StringComparison.Ordinal) &&
                IsMatch(path, normalizedPattern[3..]))
            {
                return true;
            }

            var regex = "^" + Regex.Escape(normalizedPattern)
                .Replace(@"\*\*", "<<<DOUBLESTAR>>>")
                .Replace(@"\*", @"[^/]*")
                .Replace("<<<DOUBLESTAR>>>", ".*") + "$";

            return Regex.IsMatch(path, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }
}
