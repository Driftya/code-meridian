using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed class DefaultAnalysisProfilePolicy : IAnalysisProfilePolicy
{
    public bool Allows(AnalysisProfile profile, IndexedFileRole role) => profile switch
    {
        AnalysisProfile.FullInventory => true,
        AnalysisProfile.DesignSmells => role == IndexedFileRole.Source,
        AnalysisProfile.TestShield => role is IndexedFileRole.Source or IndexedFileRole.Test,
        AnalysisProfile.CoverageGaps => role is IndexedFileRole.Source or IndexedFileRole.Test,
        AnalysisProfile.Architecture => role == IndexedFileRole.Source,
        AnalysisProfile.DuplicateDetection => role == IndexedFileRole.Source,
        AnalysisProfile.Documentation => role is IndexedFileRole.Documentation or IndexedFileRole.Configuration or IndexedFileRole.Source,
        AnalysisProfile.AgentContext => role is IndexedFileRole.Source or IndexedFileRole.Test or IndexedFileRole.Documentation or IndexedFileRole.Configuration,
        _ => role != IndexedFileRole.BuildArtifact
    };
}
