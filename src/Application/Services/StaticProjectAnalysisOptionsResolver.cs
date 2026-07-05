using System.Text.Json;

namespace CodeMeridian.Application.Services;

internal sealed class StaticProjectAnalysisOptionsResolver(CodebaseAnalysisOptions options) : IProjectAnalysisOptionsResolver
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly CodebaseAnalysisOptions options = Clone(options);

    public ValueTask<ResolvedProjectAnalysisOptions> ResolveAsync(string? projectContext, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ResolvedProjectAnalysisOptions(
            Clone(options),
            new AnalysisOptionsResolutionMetadata(
                UsedGlobalConfig: false,
                UsedProjectConfig: false,
                Warnings: [])));

    private static CodebaseAnalysisOptions Clone(CodebaseAnalysisOptions source) =>
        JsonSerializer.Deserialize<CodebaseAnalysisOptions>(
            JsonSerializer.Serialize(source, SerializerOptions),
            SerializerOptions)
        ?? new CodebaseAnalysisOptions();
}
