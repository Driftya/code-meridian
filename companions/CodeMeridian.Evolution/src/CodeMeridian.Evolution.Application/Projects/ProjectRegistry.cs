namespace CodeMeridian.Evolution.Application.Projects;

public sealed class ProjectRegistry
{
    private readonly IReadOnlyList<ProjectDescriptor> projects =
        Array.AsReadOnly(
        [
            new ProjectDescriptor(
                "meridian-evolution",
                "Meridian Evolution",
                "self: the persistent cognitive entity",
                MayPrepareChanges: true,
                RequiresHumanApproval: true),
            new ProjectDescriptor(
                "codemeridian",
                "CodeMeridian",
                "external project and bounded cognitive tool",
                MayPrepareChanges: true,
                RequiresHumanApproval: true)
        ]);

    public IReadOnlyList<ProjectDescriptor> List()
    {
        return projects;
    }

    public ProjectDescriptor Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return projects.FirstOrDefault(project =>
                   string.Equals(project.Id, id, StringComparison.Ordinal))
               ?? throw new KeyNotFoundException($"Project '{id}' is not registered.");
    }
}
