using CodeMeridian.Core.Projects;

namespace CodeMeridian.Indexer.Cli.Commands;

internal static class ProducerScopePlanner
{
    public static IReadOnlyList<ProducerScopeSelection> Select(DirectoryInfo repository, IEnumerable<DirectoryInfo> roots,
        IEnumerable<string> inventoryPaths)
    {
        var scopes = roots.Select(root => Path.GetRelativePath(repository.FullName, root.FullName).Replace('\\', '/'))
            .Select(scope => scope == "." ? scope : ProjectProfilePath.Normalize(scope)).Distinct(StringComparer.Ordinal)
            .OrderByDescending(scope => scope.Length).ThenBy(scope => scope, StringComparer.Ordinal).ToArray();
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var path in inventoryPaths.Order(StringComparer.Ordinal))
        {
            if (ProjectProfilePath.Normalize(path) != path) throw new ArgumentException("Inventory identities must be canonical.");
            var scope = scopes.FirstOrDefault(scope => scope != "." && path.StartsWith(scope + "/", StringComparison.Ordinal)) ?? ".";
            if (!groups.TryGetValue(scope, out var paths)) groups[scope] = paths = [];
            if (paths.Contains(path, StringComparer.Ordinal)) throw new ArgumentException("Producer inventory contains duplicate identities.");
            paths.Add(path);
        }
        return groups.OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => new ProducerScopeSelection(group.Key,
            new DirectoryInfo(group.Key == "." ? repository.FullName : Path.Combine(repository.FullName, group.Key)), group.Value)).ToArray();
    }
}
