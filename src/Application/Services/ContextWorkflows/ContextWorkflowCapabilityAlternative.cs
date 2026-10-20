namespace CodeMeridian.Application.Services.ContextWorkflows;

public sealed record ContextWorkflowCapabilityAlternative(
    IReadOnlyList<string> FileKinds,
    IReadOnlyList<string> Capabilities);
