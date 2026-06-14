using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed class DefaultAnalysisProfilePolicy : IAnalysisProfilePolicy
{
    public bool Allows(AnalysisProfile profile, IndexedFileRole role) => profile switch
    {
        AnalysisProfile.FullInventory => true,
        AnalysisProfile.DesignSmells => role is IndexedFileRole.Source or IndexedFileRole.Unknown,
        AnalysisProfile.TestShield => role is IndexedFileRole.Source or IndexedFileRole.Test or IndexedFileRole.Unknown,
        AnalysisProfile.CoverageGaps => role is IndexedFileRole.Source or IndexedFileRole.Test or IndexedFileRole.Unknown,
        AnalysisProfile.Architecture => role is IndexedFileRole.Source or IndexedFileRole.Unknown,
        AnalysisProfile.DuplicateDetection => role is IndexedFileRole.Source or IndexedFileRole.Unknown,
        AnalysisProfile.Documentation => role is IndexedFileRole.Documentation or IndexedFileRole.Configuration or IndexedFileRole.Source or IndexedFileRole.Unknown,
        AnalysisProfile.AgentContext => role is IndexedFileRole.Source or IndexedFileRole.Test or IndexedFileRole.Documentation or IndexedFileRole.Configuration or IndexedFileRole.Unknown,
        _ => role != IndexedFileRole.BuildArtifact
    };
}
