namespace CodeMeridian.Core.Projects;

public sealed record ProjectProfileSnapshot(
    long Generation,
    ProjectProfile Profile,
    IReadOnlyList<ProjectInventoryFile> Files)
{
    public string ContractVersion { get; init; } = "1.0";

    public string? Validate()
    {
        if (ContractVersion != "1.0" || Profile?.ContractVersion != "1.0")
            return "Unsupported project profile contract version.";
        if (Generation <= 0) return "A server-issued positive generation is required.";
        if (!ValidName(Profile.Project, 200)) return "A nonempty project name of at most 200 characters is required.";
        if (Profile.EvidenceSource != "local_discovery") return "Only local discovery snapshots can be published.";
        if (Profile.DiscoveryState != "complete" || Profile.WarningCount != 0 || Profile.Warnings is null || Profile.Warnings.Count != 0)
            return "Only complete discovery can replace the published inventory.";
        if (Profile.ObservedAt == default) return "Discovery observation time is required.";
        if (Files is null || Files.Count > 100000 || Profile.FileCount != Files.Count)
            return "Inventory must contain at most 100000 files and match the declared file count.";
        if (Profile.FileKinds is null || Profile.FileKinds.Count > 32
            || Profile.AnalysisRoots is null || Profile.AnalysisRoots.Count > 50
            || Profile.AnalysisRootCount < Profile.AnalysisRoots.Count || Profile.AnalysisRootCount > 100000
            || Profile.Analyzers is null || Profile.Analyzers.Count > 16)
            return "Profile collections are missing or exceed their bounds.";

        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Files)
        {
            if (file is null || !ValidName(file.Kind, 64)) return "Inventory file kind is invalid.";
            try
            {
                if (ProjectProfilePath.Normalize(file.Path) != file.Path) return "Inventory paths must use forward slashes.";
            }
            catch (ArgumentException) { return "Inventory contains an invalid relative path."; }
            if (!paths.TryAdd(file.Path, file.Kind)) return "Inventory paths must be unique.";
        }

        var counts = Files.GroupBy(file => file.Kind, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kind in Profile.FileKinds)
        {
            if (kind is null || !ValidName(kind.Kind, 64) || !kinds.Add(kind.Kind)
                || !counts.TryGetValue(kind.Kind, out var count) || kind.Count != count
                || kind.ExamplePaths is null || kind.ExamplePaths.Count > 5
                || kind.ExamplePaths.Any(path => path is null || !paths.TryGetValue(path, out var actualKind) || actualKind != kind.Kind))
                return "File kind summaries must match the complete inventory.";
        }
        if (kinds.Count != counts.Count) return "Every inventory file kind needs a summary.";

        foreach (var root in Profile.AnalysisRoots)
        {
            if (root is null || !ValidName(root.Kind, 64)) return "Analysis root kind is invalid.";
            try
            {
                if (root.Path != "." && ProjectProfilePath.Normalize(root.Path) != root.Path)
                    return "Analysis root paths must use forward slashes.";
            }
            catch (ArgumentException) { return "Analysis root path is invalid."; }
            if (root.EvidencePath is not null && !paths.ContainsKey(root.EvidencePath))
                return "Analysis root evidence must refer to an inventoried file.";
        }

        var analyzers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var analyzer in Profile.Analyzers)
        {
            if (analyzer is null || !ValidName(analyzer.Id, 64) || !analyzers.Add(analyzer.Id)
                || analyzer.ConfigurationState is not ("enabled" or "disabled" or "unsupported")
                || analyzer.IndexingState != "unknown"
                || analyzer.SupportedCapabilities is null || analyzer.SupportedCapabilities.Count > 16
                || analyzer.SupportedCapabilities.Any(capability => !ValidName(capability, 64))
                || analyzer.Limitations is null || analyzer.Limitations.Count > 8
                || analyzer.Limitations.Any(limit => !ValidName(limit, 512))
                || (!analyzer.Supported && analyzer.SupportedCapabilities.Count > 0))
                return "Analyzer metadata is invalid; discovery cannot claim semantic indexing readiness.";
        }
        return null;
    }

    internal static bool ValidName(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength
        && value == value.Trim() && !value.Any(char.IsControl);
}
