using System.Text;

namespace CodeMeridian.Application.Services;

public partial class CodebaseQueryService
{
    private async Task<string> BuildProjectContextHintAsync(
        string? projectContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectContext))
            return string.Empty;

        var projectContexts = await codeGraph.GetProjectContextsAsync(projectContext, cancellationToken);
        if (projectContexts.Count == 0)
            projectContexts = await codeGraph.GetProjectContextsAsync(cancellationToken: cancellationToken);

        if (projectContexts.Count == 0)
            return string.Empty;

        var suggestion = FindClosestProjectContext(projectContext, projectContexts);
        if (suggestion is not null)
            return $" Did you mean '{suggestion}'?";

        var available = string.Join(", ", projectContexts.Take(5).Select(context => $"'{context}'"));
        return $" Available projects: {available}.";
    }

    private static string? FindClosestProjectContext(
        string requested,
        IReadOnlyCollection<string> candidates)
    {
        var normalizedRequested = NormalizeProjectContextForSuggestion(requested);
        if (normalizedRequested.Length == 0)
            return null;

        var ranked = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => new
            {
                Candidate = candidate,
                Normalized = NormalizeProjectContextForSuggestion(candidate)
            })
            .Where(candidate => candidate.Normalized.Length > 0)
            .Select(candidate => new
            {
                candidate.Candidate,
                Score = ScoreProjectContextCandidate(normalizedRequested, candidate.Normalized)
            })
            .Where(candidate => candidate.Score < int.MaxValue)
            .OrderBy(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Candidate, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return ranked?.Candidate;
    }

    private static int ScoreProjectContextCandidate(string requested, string candidate)
    {
        if (string.Equals(requested, candidate, StringComparison.Ordinal))
            return 0;

        if (candidate.Contains(requested, StringComparison.Ordinal) || requested.Contains(candidate, StringComparison.Ordinal))
            return 1;

        var distance = LevenshteinDistance(requested, candidate);
        var threshold = requested.Length <= 8 ? 1 : 2;
        return distance <= threshold ? 2 + distance : int.MaxValue;
    }

    private static string NormalizeProjectContextForSuggestion(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    private static int LevenshteinDistance(string left, string right)
    {
        if (left.Length == 0)
            return right.Length;
        if (right.Length == 0)
            return left.Length;

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var substitutionCost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
