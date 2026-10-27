using CodeMeridian.Core.Projects;
using CodeMeridian.Application.RepositoryConstraints;

namespace CodeMeridian.Application.Projects;

public sealed class ProjectProfileService(IProjectProfileRepository repository, RepositoryConstraintQueryService? constraints = null)
{
    public async Task<RepositoryConstraintQueryResult?> FindRepositoryConstraintsAsync(string projectContext, string? targetPath = null,
        CancellationToken cancellationToken = default)
        => constraints is null ? null : await constraints.FindAsync(projectContext, targetPath, cancellationToken: cancellationToken);

    public async Task<ProjectProfileResult> GetAsync(
        string projectContext, string? targetPath = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectContext) || projectContext.Length > 200
            || projectContext != projectContext.Trim() || projectContext.Any(char.IsControl))
            throw new ArgumentException("A nonempty project name of at most 200 characters is required.", nameof(projectContext));
        if (targetPath is not null) targetPath = ProjectProfilePath.Normalize(targetPath);
        var stored = await repository.GetAsync(projectContext, targetPath, cancellationToken);
        if (stored is null)
            return new ProjectProfileResult("unknown", projectContext, null, null, null, null,
                targetPath is null ? null : new ProjectProfileTarget(targetPath, "unknown", null), []);
        var stale = stored.LatestGeneration > stored.Generation;
        RepositoryConstraintCapabilitySummary? constraintSummary = null;
        if (!stale && constraints is not null)
        {
            try
            {
                var constraintResult = await FindRepositoryConstraintsAsync(projectContext, targetPath, cancellationToken);
                constraintSummary = RepositoryConstraintCapabilitySummary.From(constraintResult!, stored.Generation);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                constraintSummary = new(false, "failed", "unknown", stored.Generation, [], ["Repository constraint evidence could not be read."]);
            }
        }
        return new ProjectProfileResult(stale ? "stale" : "available", projectContext, stored.Generation,
            stored.LatestGeneration, stored.PublishedAt, stored.Profile,
            targetPath is null ? null : new ProjectProfileTarget(targetPath,
                stored.Target is not null ? "observed" : stale ? "unknown" : "absent", stored.Target?.Kind),
            stale ? ["A newer discovery generation was reserved but has not been published. The previous complete inventory is retained."] : [])
        {
            AnalyzerOutcomes = stored.AnalyzerOutcomes,
            RepositoryConstraints = constraintSummary
        };
    }
}
