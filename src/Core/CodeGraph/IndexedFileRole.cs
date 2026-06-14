namespace CodeMeridian.Core.CodeGraph;

public enum IndexedFileRole
{
    Source,
    Test,
    Migration,
    Snapshot,
    Generated,
    BuildArtifact,
    Documentation,
    Configuration,
    Unknown
}

public enum AnalysisProfile
{
    FullInventory,
    AgentContext,
    DesignSmells,
    TestShield,
    CoverageGaps,
    Architecture,
    DuplicateDetection,
    Documentation
}
