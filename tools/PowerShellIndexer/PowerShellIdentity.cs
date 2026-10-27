namespace CodeMeridian.PowerShellIndexer;

public static class PowerShellIdentity
{
    public static string File(string project, string path) => $"{project}::PowerShellFile::{PowerShellWorkerIdentity.Hash(path)[7..]}";
    public static string Body(string fileId) => fileId + "::body";
    public static string Definition(string ownerId, string kind, string name, int ordinal) =>
        $"{ownerId[..ownerId.IndexOf("::", StringComparison.Ordinal)]}::PowerShellBody::{PowerShellWorkerIdentity.Hash($"{ownerId}\0{kind}\0{name.ToUpperInvariant()}\0{ordinal}")[7..]}";
}
