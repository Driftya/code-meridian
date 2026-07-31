namespace CodeMeridian.Evolution.Application.Projects;

public sealed record ProjectDescriptor(
    string Id,
    string DisplayName,
    string Relationship,
    bool MayPrepareChanges,
    bool RequiresHumanApproval);
