namespace CodeMeridian.Application.Services;

public sealed class NullGlobalAnalysisConfigurationSource : IGlobalAnalysisConfigurationSource
{
    private static readonly AnalysisConfigurationSourceResult Empty = new([], []);

    public ValueTask<AnalysisConfigurationSourceResult> LoadAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Empty);
}
