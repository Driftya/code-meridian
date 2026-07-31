namespace CodeMeridian.Evolution.Application.Projects;

public sealed record CandidateApprovalRequest(
    string Actor,
    string Reason,
    string IdempotencyKey);
