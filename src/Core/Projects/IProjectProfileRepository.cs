namespace CodeMeridian.Core.Projects;

public interface IProjectProfileRepository
{
    Task<long> BeginAsync(string project, CancellationToken cancellationToken = default);
    Task PublishAsync(ProjectProfileSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<StoredProjectProfile?> GetAsync(string project, string? targetPath = null, CancellationToken cancellationToken = default);
}
