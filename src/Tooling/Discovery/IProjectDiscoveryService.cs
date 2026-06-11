namespace CodeMeridian.Tooling.Discovery;

public interface IProjectDiscoveryService
{
    bool ContainsFile(DirectoryInfo root, params string[] extensions);
    IReadOnlyList<DirectoryInfo> FindTypeScriptRoots(DirectoryInfo root);
    DirectoryInfo? FindRepositoryRoot(DirectoryInfo start);
    string ResolveProjectName(DirectoryInfo root);
}
