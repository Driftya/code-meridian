using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodeMeridian.Core.Projects;
using CodeMeridian.DocumentIndexer.Pipeline;
using CodeMeridian.RoslynIndexer.Pipeline;

namespace CodeMeridian.Indexer.Cli.Commands;

internal static class ProjectAnalysisFingerprints
{
    public static string Configuration(ResolvedIndexerSettings settings) => Hash(JsonSerializer.Serialize(new
    {
        // Only resolved indexing options belong here; credentials and transport/cache settings do not.
        settings.SkipCSharp, settings.SkipTypeScript, settings.IncludeDocs, settings.SkipConfiguration, settings.SkipSql,
        settings.Sql, settings.ConfigurationFiles, settings.ArchitecturePath, settings.FileRoles,
        settings.AllowRepoScripts, settings.SkipDiagnostics, settings.RebuildKeywords,
        PackageProducerBindings = settings.PackageProducerBindings?.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray()
    }));

    public static ProjectAnalyzerFingerprint? Analyzer(string analyzer)
    {
        Assembly[] parsers = analyzer switch
        {
            "csharp" => [typeof(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree).Assembly, typeof(Microsoft.CodeAnalysis.SyntaxTree).Assembly],
            "documents" => [typeof(DocumentIndexerPipeline).Assembly],
            "configuration" => [typeof(ProjectAnalysisFingerprints).Assembly, typeof(YamlDotNet.Core.Parser).Assembly, typeof(JsonDocument).Assembly],
            _ => [] // Node identities are supplied separately by executing the worker's describe mode.
        };
        if (parsers.Length == 0) return null;
        var implementation = analyzer == "csharp" ? typeof(IndexerPipeline).Assembly
            : analyzer == "documents" ? typeof(DocumentIndexerPipeline).Assembly : typeof(ProjectAnalysisFingerprints).Assembly;
        return new(Modules([implementation, typeof(ProjectAnalysisFingerprints).Assembly,
            typeof(CodeMeridian.Sdk.CodeMeridianClient).Assembly, typeof(CodeMeridian.Tooling.Discovery.RepositoryInventoryDiscovery).Assembly,
            typeof(CodeMeridian.Application.Services.IIndexedFileRoleClassifier).Assembly, typeof(ProjectProfile).Assembly]), Modules(parsers));
    }

    private static string Modules(IEnumerable<Assembly> assemblies) => Hash(string.Join("\n", assemblies.Distinct()
        .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
        .Select(assembly => $"{assembly.GetName().Name}:{assembly.GetName().Version}:{assembly.ManifestModule.ModuleVersionId:D}")));

    private static string Hash(string value) => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
