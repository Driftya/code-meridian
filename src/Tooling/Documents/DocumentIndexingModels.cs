namespace CodeMeridian.Tooling.Documents;

public sealed record DocumentChunk(
    string Content,
    string RelativePath,
    int ChunkIndex,
    int ChunkCount);

public sealed record DocumentReference(
    string SourcePath,
    string TargetPath,
    string LinkText);
