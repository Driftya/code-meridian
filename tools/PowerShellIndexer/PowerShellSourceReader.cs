using System.Text;
using CodeMeridian.Core.Projects;

namespace CodeMeridian.PowerShellIndexer;

internal static class PowerShellSourceReader
{
    internal const int MaximumBytes = 1048576;

    internal static async Task<byte[]> ReadAsync(DirectoryInfo root, string path, CancellationToken cancellationToken)
    {
        path = ProjectProfilePath.Normalize(path);
        FileSystemInfo entry = new FileInfo(Path.Combine(root.FullName, path));
        while (true)
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Symbolic links and junctions are outside the analysis boundary.");
            if (entry.FullName == root.FullName) break;
            entry = entry is FileInfo file ? file.Directory! : ((DirectoryInfo)entry).Parent
                ?? throw new IOException("The source path is outside the root.");
        }
        await using var stream = new FileStream(Path.Combine(root.FullName, path), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        var buffer = new byte[MaximumBytes + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (count == 0) break;
            read += count;
        }
        if (read > MaximumBytes) throw new InvalidDataException("Source exceeds the per-file byte budget.");
        return buffer[..read];
    }

    internal static string Decode(byte[] bytes)
    {
        var offset = 0;
        Encoding encoding = new UTF8Encoding(false, true);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) { encoding = new UTF32Encoding(false, false, true); offset = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) { encoding = new UTF32Encoding(true, false, true); offset = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, false, true); offset = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, false, true); offset = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) offset = 3;
        var text = encoding.GetString(bytes, offset, bytes.Length - offset);
        if (text.Contains('\0')) throw new DecoderFallbackException("NUL and BOM-less UTF-16/32 inputs are unsupported.");
        return text;
    }
}
