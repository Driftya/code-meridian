namespace CodeMeridian.PowerShellIndexer.Tests;

internal sealed class LinuxSymlinkFactAttribute : FactAttribute
{
    public LinuxSymlinkFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Windows link creation requires an unavailable privilege; the Linux packaged smoke covers this boundary.";
    }
}
