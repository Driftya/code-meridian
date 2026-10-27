using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CodeMeridian.Indexer.Cli.Commands;

internal static class DirectoryCaseSensitivity
{
    // Windows exposes per-directory case flags. Other platforms remain unknown until audited.
    public static bool? Read(DirectoryInfo root, IReadOnlyList<string> directories, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return null;
        bool? result = null;
        foreach (var path in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var handle = CreateFileW(Path.Combine(root.FullName, path), 0x80, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
            if (handle.IsInvalid || !GetFileInformationByHandleEx(handle, 23, out var flags, sizeof(uint))) return null;
            var sensitive = (flags & 1) != 0;
            if (result is not null && result != sensitive) return null;
            result = sensitive;
        }
        return result;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out uint information, uint size);
}
