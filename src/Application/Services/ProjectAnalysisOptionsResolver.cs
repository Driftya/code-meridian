using System.Reflection;
using System.Text.Json;
using CodeMeridian.Core.CodeGraph;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Application.Services;

public sealed class ProjectAnalysisOptionsResolver(
    ICodeGraphRepository codeGraph,
    IGlobalAnalysisConfigurationSource globalSource,
    IOptions<CodebaseAnalysisOptions> runtimeOptions,
    ILogger<ProjectAnalysisOptionsResolver> logger) : IProjectAnalysisOptionsResolver
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly CodebaseAnalysisOptions runtimeOptions = Clone(runtimeOptions.Value);

    public async ValueTask<ResolvedProjectAnalysisOptions> ResolveAsync(
        string? projectContext,
        CancellationToken cancellationToken = default)
    {
        var resolved = Clone(runtimeOptions);
        var warnings = new List<string>();

        var globalResult = await globalSource.LoadAsync(cancellationToken);
        warnings.AddRange(globalResult.Warnings);
        var usedGlobalConfig = ApplyEntries(resolved, globalResult.Entries, globalResult.SourceDescription ?? "global meridian.json", warnings);

        var usedProjectConfig = false;
        if (!string.IsNullOrWhiteSpace(projectContext))
        {
            var projectResult = await LoadProjectEntriesAsync(projectContext, cancellationToken);
            warnings.AddRange(projectResult.Warnings);
            usedProjectConfig = ApplyEntries(resolved, projectResult.Entries, $"project '{projectContext}'", warnings);
        }

        if (warnings.Count > 0)
        {
            logger.LogWarning(
                "Resolved analysis options for {ProjectContext} with warnings. UsedGlobalConfig={UsedGlobalConfig}, UsedProjectConfig={UsedProjectConfig}, Warnings={Warnings}",
                projectContext ?? "<none>",
                usedGlobalConfig,
                usedProjectConfig,
                string.Join(" | ", warnings));
        }
        else
        {
            logger.LogDebug(
                "Resolved analysis options for {ProjectContext}. UsedGlobalConfig={UsedGlobalConfig}, UsedProjectConfig={UsedProjectConfig}",
                projectContext ?? "<none>",
                usedGlobalConfig,
                usedProjectConfig);
        }

        return new ResolvedProjectAnalysisOptions(
            resolved,
            new AnalysisOptionsResolutionMetadata(
                usedGlobalConfig,
                usedProjectConfig,
                warnings));
    }

    private async Task<AnalysisConfigurationSourceResult> LoadProjectEntriesAsync(
        string projectContext,
        CancellationToken cancellationToken)
    {
        var nodes = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                ProjectContext = projectContext,
                FilePathFilter = "meridian.json",
                TypeFilter = CodeNodeType.ConfigurationEntry,
                Limit = 1000
            },
            cancellationToken);

        var warnings = new List<string>();
        var entries = new List<AnalysisConfigurationEntry>();

        foreach (var node in nodes.Where(node => string.Equals(node.FilePath, "meridian.json", StringComparison.OrdinalIgnoreCase)))
        {
            if (!node.Properties.TryGetValue("canonicalKey", out var canonicalKey)
                || string.IsNullOrWhiteSpace(canonicalKey)
                || !canonicalKey.StartsWith("analysis:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!node.Properties.TryGetValue("rawValuePreview", out var value))
                value = null;

            if (string.Equals(value, "***", StringComparison.Ordinal))
            {
                warnings.Add($"Skipped masked project analysis entry `{canonicalKey}` in `meridian.json`.");
                continue;
            }

            entries.Add(new AnalysisConfigurationEntry(canonicalKey, value));
        }

        return new AnalysisConfigurationSourceResult(entries, warnings, $"project `meridian.json` for '{projectContext}'");
    }

    private static bool ApplyEntries(
        CodebaseAnalysisOptions target,
        IReadOnlyList<AnalysisConfigurationEntry> entries,
        string sourceDescription,
        ICollection<string> warnings)
    {
        if (entries.Count == 0)
            return false;

        foreach (var group in entries
                     .Where(entry => !string.IsNullOrWhiteSpace(entry.CanonicalKey))
                     .GroupBy(entry => GetFieldRoot(entry.CanonicalKey), StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                ResetCollectionField(target, group.Key);

                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(group.ToDictionary(entry => entry.CanonicalKey, entry => entry.Value, StringComparer.OrdinalIgnoreCase))
                    .Build();

                configuration.GetSection("analysis").Bind(target);
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to apply {sourceDescription} override `{group.Key}`: {ex.Message}");
            }
        }

        return true;
    }

    private static string GetFieldRoot(string canonicalKey)
    {
        var segments = canonicalKey.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length <= 3)
            return canonicalKey;

        return string.Join(':', segments[..3]);
    }

    private static void ResetCollectionField(CodebaseAnalysisOptions target, string fieldRoot)
    {
        var segments = fieldRoot.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 3 || !segments[0].Equals("analysis", StringComparison.OrdinalIgnoreCase))
            return;

        var sectionProperty = FindProperty(typeof(CodebaseAnalysisOptions), segments[1]);
        if (sectionProperty is null)
            return;

        var sectionInstance = sectionProperty.GetValue(target);
        if (sectionInstance is null)
        {
            sectionInstance = Activator.CreateInstance(sectionProperty.PropertyType);
            if (sectionInstance is null)
                return;

            sectionProperty.SetValue(target, sectionInstance);
        }

        var valueProperty = FindProperty(sectionProperty.PropertyType, segments[2]);
        if (valueProperty is null)
            return;

        if (!IsResettableCollection(valueProperty.PropertyType))
            return;

        var resetValue = Activator.CreateInstance(valueProperty.PropertyType);
        valueProperty.SetValue(sectionInstance, resetValue);
    }

    private static PropertyInfo? FindProperty(Type type, string name) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));

    private static bool IsResettableCollection(Type type) =>
        type != typeof(string)
        && type.IsClass
        && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);

    private static CodebaseAnalysisOptions Clone(CodebaseAnalysisOptions source) =>
        JsonSerializer.Deserialize<CodebaseAnalysisOptions>(
            JsonSerializer.Serialize(source, SerializerOptions),
            SerializerOptions)
        ?? new CodebaseAnalysisOptions();
}
