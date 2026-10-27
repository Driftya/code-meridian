using System.Management.Automation.Language;
using System.Security.Cryptography;
using System.Text;
using System.Reflection;

namespace CodeMeridian.PowerShellIndexer;

public sealed record PowerShellWorkerIdentity(string PackageVersion, string ParserVersion, string Runtime,
    string ParserFingerprint, string ImplementationFingerprint)
{
    public string ContractVersion { get; init; } = "1.0";
    public string ParseMode { get; init; } = PowerShellSyntaxParser.Mode;
    public string ParserBuild { get; init; } = typeof(Parser).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    public static PowerShellWorkerIdentity Read() => new("7.6.8", typeof(Parser).Assembly.GetName().Version!.ToString(),
        System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        Hash(File.ReadAllBytes(typeof(Parser).Assembly.Location)),
        Hash(File.ReadAllBytes(typeof(PowerShellWorkerIdentity).Assembly.Location)
            .Concat(File.ReadAllBytes(typeof(CodeMeridian.Core.CodeGraph.CodeNode).Assembly.Location)).ToArray()));

    internal static string Hash(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));
}
