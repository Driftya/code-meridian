using CodeMeridian.Core.Projects;

namespace CodeMeridian.PowerShellIndexer;

public sealed class PowerShellAnalyzer
{
    public const int MaximumFiles = 1000;
    public const int MaximumTotalBytes = 16777216;
    public const int MaximumTotalFacts = 20000;

    public async Task<PowerShellAnalysisReport> AnalyzeAsync(PowerShellAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.ContractVersion != "1.0" || string.IsNullOrWhiteSpace(request.Project) || request.Project.Length > 200 || request.Project != request.Project.Trim()
            || request.Project.Contains("::", StringComparison.Ordinal) || request.Project.Any(char.IsControl) || request.CaseSensitive is null
            || request.Files is null || request.Files.Count > MaximumFiles || !Path.IsPathFullyQualified(request.RootPath))
            throw new ArgumentException("Use an explicit project/root/case policy and at most 1000 canonical source paths.");
        var root = new DirectoryInfo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.RootPath)));
        if (!root.Exists || (root.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("The root must be an existing directory without a reparse boundary.");
        for (var parent = root.Parent; parent is not null; parent = parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Root ancestors cannot cross symbolic links or junctions.");
        var comparer = request.CaseSensitive.Value ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var paths = new HashSet<string>(comparer);
        foreach (var path in request.Files)
        {
            if (ProjectProfilePath.Normalize(path) != path || !paths.Add(path)
                || Path.GetExtension(path).ToLowerInvariant() is not (".ps1" or ".psm1" or ".psd1"))
                throw new ArgumentException("Sources must be unique canonical .ps1/.psm1/.psd1 paths under the declared case semantics.");
        }
        var files = new List<PowerShellFileAnalysis>();
        var totalBytes = 0;
        var totalFacts = 0;
        foreach (var path in request.Files.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? hash = null;
            try
            {
                if (totalBytes >= MaximumTotalBytes || totalFacts >= MaximumTotalFacts)
                    throw new InvalidDataException("The analysis exhausted its source/fact budget.");
                var bytes = await PowerShellSourceReader.ReadAsync(root, path, cancellationToken);
                hash = PowerShellWorkerIdentity.Hash(bytes);
                totalBytes += bytes.Length;
                if (totalBytes > MaximumTotalBytes) throw new InvalidDataException("The analysis exceeds its total source-byte budget.");
                var parsed = PowerShellParser.Parse(request.Project, path, PowerShellSourceReader.Decode(bytes), hash, cancellationToken);
                totalFacts += parsed.Bodies.Count + parsed.Commands.Count + parsed.Modules.Count;
                if (totalFacts > MaximumTotalFacts) throw new InvalidDataException("The analysis exhausted its source/fact budget.");
                files.Add(parsed);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
            {
                files.Add(new(request.Project, path, PowerShellIdentity.File(request.Project, path), hash, "failed", 0,
                    [], [], [], null, null, [new("source_unavailable", exception is System.Text.DecoderFallbackException
                        ? "Source encoding is unsupported or malformed; no facts were retained."
                        : "Source could not be read within the declared path and byte budgets; no facts were retained.", null)], 1));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        // Binding never reads additional source files or follows imports outside this explicit selection.
        var bound = PowerShellBinder.Bind(files, request.CaseSensitive.Value, cancellationToken);
        return new(request.Project, bound.Any(file => file.Status != "complete") ? "partial" : "complete",
            PowerShellWorkerIdentity.Read(), request.Files.Count, bound);
    }
}
