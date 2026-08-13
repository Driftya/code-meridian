namespace CodeMeridian.McpServer.Tools;

public sealed record ChangeContextChallengeNoteResult(
    string ContractVersion,
    string ChallengeId,
    string ContextId,
    string NodeId,
    string ContextKind,
    string Provenance,
    string Status);
