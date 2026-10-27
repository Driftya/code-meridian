using System.Text;
using CodeMeridian.PowerShellIndexer;

namespace CodeMeridian.PowerShellIndexer.Tests;

internal sealed class PowerShellWorkspace : IDisposable
{
    internal DirectoryInfo Root { get; } = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"meridian-powershell-{Guid.NewGuid():N}"));
    internal string Write(string path, string text, Encoding? encoding = null)
    {
        var absolute = Path.Combine(Root.FullName, path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllText(absolute, text, encoding ?? new UTF8Encoding(false));
        return absolute;
    }
    internal Task<PowerShellAnalysisReport> Analyze(params string[] files) =>
        new PowerShellAnalyzer().AnalyzeAsync(new("Fixture", Root.FullName, true, files));
    public void Dispose() => Root.Delete(recursive: true);
}
