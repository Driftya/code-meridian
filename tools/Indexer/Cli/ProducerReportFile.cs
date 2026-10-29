using System.Text.Json;
using CodeMeridian.Core.Projects;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class ProducerReportFile(string analyzer, string project, string scope,
    ProjectAnalyzerFingerprint fingerprint) : IDisposable
{
    private readonly string requestId = Guid.NewGuid().ToString("N");
    private readonly string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "codemeridian-producer-" + Guid.NewGuid().ToString("N") + ".json");
    public bool Exists => File.Exists(path);

    public IReadOnlyDictionary<string, string?> Environment => new Dictionary<string, string?>
    {
        [NodeWorkerIdentity.ExpectedFingerprintVariable] = NodeWorkerIdentity.Expected(fingerprint),
        ["CODEMERIDIAN_PRODUCER_REPORT"] = path,
        ["CODEMERIDIAN_PRODUCER_REQUEST_ID"] = requestId,
        ["CODEMERIDIAN_PRODUCER_SCOPE"] = scope
    };

    public async Task<ProducerExecutionReport> ReadAsync(CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 4 * 1024 * 1024) throw new InvalidDataException("Producer report is missing or exceeds its budget.");
        var report = JsonSerializer.Deserialize<ProducerExecutionReport>(await File.ReadAllTextAsync(path, cancellationToken),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Producer report is missing.");
        if (report.ContractVersion != "1" || report.RequestId != requestId || report.Analyzer != analyzer
            || report.Project != project || report.Scope != scope || report.Fingerprint != fingerprint
            || report.Files is null || report.Files.Count > 10000 || report.Capabilities is null || report.Capabilities.Count > 16
            || report.Capabilities.Any(item => item.Key.Length > 64 || item.Value is not ("complete" or "partial" or "unknown" or "unsupported"))
            || report.Limitations is null || report.Limitations.Count > 16 || report.Limitations.Any(item => item is null || item.Length > 512)
            || report.Files.Any(item => item is null || item.Status is not ("complete" or "partial" or "failed")
                || item.Diagnostics is null || item.Diagnostics.Count > 8 || item.Diagnostics.Any(value => value is null || value.Length > 512)))
            throw new InvalidDataException("Producer report identity, outcomes or bounds are invalid.");
        return report;
    }

    public void Dispose() => File.Delete(path);
}
