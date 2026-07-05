namespace CodeMeridian.Application.Services;

public interface IProjectAnalysisOptionsResolver
{
    ValueTask<ResolvedProjectAnalysisOptions> ResolveAsync(string? projectContext, CancellationToken cancellationToken = default);
}
