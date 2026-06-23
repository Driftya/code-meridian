using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private async Task<ResponsibilityCommunityLookup> TryGetResponsibilityCommunitiesAsync(
        IReadOnlyList<ResponsibilityMethodSignals> methodSignals,
        string? projectContext,
        CancellationToken cancellationToken)
    {
        var nodeIds = methodSignals
            .Select(signal => signal.Method.Id)
            .Concat(methodSignals.SelectMany(signal => signal.Dependencies).Select(node => node.Id))
            .Concat(methodSignals.SelectMany(signal => signal.ProductionCallers).Select(node => node.Id))
            .Concat(methodSignals.SelectMany(signal => signal.RelatedTests).Select(node => node.Id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (nodeIds.Length < 2)
            return ResponsibilityCommunityLookup.Empty;

        try
        {
            var assignments = await codeGraph.FindNaturalModuleAssignmentsAsync(
                nodeIds,
                methodSignals[0].Method.ProjectContext ?? projectContext,
                cancellationToken);
            return ResponsibilityCommunityLookup.Create(assignments);
        }
        catch (Exception)
        {
            return ResponsibilityCommunityLookup.Unavailable(
                "Community detection advisory evidence is unavailable; results rely on deterministic caller, dependency, test, and workflow signals only.");
        }
    }

    private static ResponsibilityCommunityAdvice BuildResponsibilityCommunityAdvice(
        IReadOnlyList<ResponsibilityMethodSignals> methods,
        ResponsibilityCommunityLookup communities)
    {
        if (!communities.HasAssignments)
            return ResponsibilityCommunityAdvice.None;

        var entries = methods
            .SelectMany(BuildCommunityEntries)
            .Select(entry => communities.TryGetCommunity(entry.Node.Id, out var communityId)
                ? entry with { CommunityId = communityId }
                : entry)
            .Where(entry => entry.CommunityId is not null)
            .GroupBy(entry => entry.CommunityId!.Value)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .FirstOrDefault();

        if (entries is null)
            return ResponsibilityCommunityAdvice.None;

        var grouped = entries.ToArray();
        var distinctMethodCount = grouped
            .Select(entry => entry.MethodId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var driver = grouped
            .GroupBy(entry => entry.Kind, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => CommunityDriverPriority(group.Key))
            .First()
            .Key;
        var kinds = grouped
            .Select(entry => entry.Kind)
            .Distinct(StringComparer.Ordinal)
            .Count();

        var advisoryBonus = 0;
        if (distinctMethodCount >= 2)
            advisoryBonus++;
        if (kinds >= 2)
            advisoryBonus++;
        if (driver is "repositories" or "dependencies" or "workflow entry points" or "callers")
            advisoryBonus++;
        if (driver == "tests")
            advisoryBonus = Math.Min(advisoryBonus, 1);

        return new ResponsibilityCommunityAdvice(
            HasSignal: true,
            Bonus: advisoryBonus,
            Summary: $"community {entries.Key} mostly reflects {driver} across {distinctMethodCount} methods");
    }

    private static IEnumerable<ResponsibilityCommunityEntry> BuildCommunityEntries(ResponsibilityMethodSignals signal)
    {
        yield return new ResponsibilityCommunityEntry(signal.Method.Id, signal.Method, signal.Method.Id, "methods", null);

        foreach (var dependency in signal.Dependencies.DistinctBy(node => node.Id))
        {
            yield return new ResponsibilityCommunityEntry(
                signal.Method.Id,
                dependency,
                signal.Method.Id,
                LooksLikeRepositoryDependency(dependency) ? "repositories" : "dependencies",
                null);
        }

        foreach (var caller in signal.WorkflowCallers.DistinctBy(node => node.Id))
            yield return new ResponsibilityCommunityEntry(signal.Method.Id, caller, signal.Method.Id, "workflow entry points", null);

        foreach (var caller in signal.ProductionCallers
                     .Where(node => signal.WorkflowCallers.All(workflow => !string.Equals(workflow.Id, node.Id, StringComparison.Ordinal)))
                     .DistinctBy(node => node.Id))
            yield return new ResponsibilityCommunityEntry(signal.Method.Id, caller, signal.Method.Id, "callers", null);

        foreach (var test in signal.RelatedTests.DistinctBy(node => node.Id))
            yield return new ResponsibilityCommunityEntry(signal.Method.Id, test, signal.Method.Id, "tests", null);
    }

    private static bool LooksLikeRepositoryDependency(CodeNode node)
    {
        var text = $"{node.Name} {node.Namespace} {node.FilePath}";
        return text.Contains("Repository", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Store", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Db", StringComparison.OrdinalIgnoreCase);
    }

    private static int CommunityDriverPriority(string kind) => kind switch
    {
        "repositories" => 0,
        "dependencies" => 1,
        "workflow entry points" => 2,
        "callers" => 3,
        "tests" => 4,
        _ => 5
    };

    private sealed record ResponsibilityCommunityEntry(
        string EntryId,
        CodeNode Node,
        string MethodId,
        string Kind,
        long? CommunityId);

    private sealed record ResponsibilityCommunityLookup(
        IReadOnlyDictionary<string, long> CommunityByNodeId,
        string? Warning)
    {
        public static ResponsibilityCommunityLookup Empty { get; } =
            new(new Dictionary<string, long>(StringComparer.Ordinal), null);

        public bool HasAssignments => CommunityByNodeId.Count > 0;

        public static ResponsibilityCommunityLookup Create(IReadOnlyList<(CodeNode Node, long Community)> assignments) =>
            new(assignments
                    .GroupBy(item => item.Node.Id, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First().Community, StringComparer.Ordinal),
                null);

        public static ResponsibilityCommunityLookup Unavailable(string warning) =>
            new(new Dictionary<string, long>(StringComparer.Ordinal), warning);

        public bool TryGetCommunity(string nodeId, out long communityId) =>
            CommunityByNodeId.TryGetValue(nodeId, out communityId);
    }

    private sealed record ResponsibilityCommunityAdvice(
        bool HasSignal,
        int Bonus,
        string Summary)
    {
        public static ResponsibilityCommunityAdvice None { get; } = new(false, 0, string.Empty);
    }
}
