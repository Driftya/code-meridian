namespace CodeMeridian.Application.Services;

public sealed record TestShieldFindingResult(
    GraphNodeResult TestNode,
    GraphNodeResult ProtectedNode,
    string MatchType,
    string Reason,
    string EvidencePath);
