namespace CodeMeridian.Application.Services;

public interface IGlobalAnalysisConfigurationSource
{
    ValueTask<AnalysisConfigurationSourceResult> LoadAsync(CancellationToken cancellationToken = default);
}
