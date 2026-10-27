using System.Management.Automation.Language;

namespace CodeMeridian.PowerShellIndexer;

internal sealed class UnsupportedPowerShellSyntaxException(IScriptExtent extent) : Exception
{
    internal IScriptExtent Extent { get; } = extent;
}
