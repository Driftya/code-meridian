using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodeMeridian.Core.Projects;

namespace CodeMeridian.Infrastructure.Graph;

internal static class ProjectProfilePublicationFingerprint
{
    public static string? BeforeSourceBinding(ProjectProfileSnapshot snapshot)
    {
        if (snapshot.Profile.SourceFingerprint is not null || snapshot.ConstraintEvidence is not null || snapshot.MarkdownEvidence is not null) return null;
        var profile = snapshot.Profile;
        var json = JsonSerializer.Serialize(new
        {
            snapshot.Generation,
            Profile = new
            {
                profile.Project, profile.ObservedAt, profile.DiscoveryState, profile.FileCount,
                profile.FileKinds, profile.AnalysisRoots, profile.AnalysisRootCount, profile.Analyzers,
                profile.Warnings, profile.WarningCount, profile.ContractVersion, profile.EvidenceSource,
                profile.Revision, profile.ConfigurationFingerprint
            },
            snapshot.Files, snapshot.ContractVersion
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    public static string? Legacy(ProjectProfileSnapshot snapshot)
    {
        if (snapshot.MarkdownEvidence is not null || snapshot.ConstraintEvidence is not null || snapshot.Profile.Revision is not null || snapshot.Profile.ConfigurationFingerprint is not null || snapshot.Profile.SourceFingerprint is not null
            || snapshot.Profile.Analyzers.Any(analyzer => analyzer.Fingerprint is not null)) return null;
        // Older servers hashed the same 1.0 payload before these optional properties existed.
        var profile = snapshot.Profile;
        var legacyJson = JsonSerializer.Serialize(new
        {
            snapshot.Generation,
            Profile = new
            {
                profile.Project, profile.ObservedAt, profile.DiscoveryState, profile.FileCount,
                profile.FileKinds, profile.AnalysisRoots, profile.AnalysisRootCount,
                Analyzers = profile.Analyzers.Select(analyzer => new
                {
                    analyzer.Id, analyzer.Supported, analyzer.ConfigurationState, analyzer.IndexingState,
                    analyzer.SupportedCapabilities, analyzer.Limitations
                }).ToArray(),
                profile.Warnings, profile.WarningCount, profile.ContractVersion, profile.EvidenceSource
            },
            snapshot.Files, snapshot.ContractVersion
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyJson)));
    }
}
