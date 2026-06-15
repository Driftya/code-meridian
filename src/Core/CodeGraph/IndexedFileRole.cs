namespace CodeMeridian.Core.CodeGraph;

public enum IndexedFileRole
{
    Source,
    Test,
    Migration,
    Snapshot,
    Generated,
    BuildArtifact,
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
