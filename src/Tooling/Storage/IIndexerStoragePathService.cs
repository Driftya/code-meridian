namespace CodeMeridian.Tooling.Storage;

public interface IIndexerStoragePathService
{
    DirectoryInfo ResolveCacheDirectory(DirectoryInfo root, string projectName, IndexerStorageMode storageMode);
    string ResolveProjectKey(DirectoryInfo root, string projectName);
}
