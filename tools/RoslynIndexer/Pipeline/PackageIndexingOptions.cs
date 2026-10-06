namespace CodeMeridian.RoslynIndexer.Pipeline;

public sealed class PackageIndexingOptions
{
    public bool AllowProjectEvaluation { get; init; }
    public IReadOnlyDictionary<string, string> ProducerBindings { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
