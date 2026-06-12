namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed record DocumentIndexRunPlan(
    IReadOnlyList<FileInfo> FilesToIngest,
    IReadOnlyList<string> FilesToDelete);

internal static class DocumentIndexRunCoordinator
{
    public static DocumentIndexRunPlan BuildPlan(
        DirectoryInfo rootPath,
        IReadOnlyCollection<string>? changedFiles,
        IReadOnlyCollection<string> deletedFiles)
    {
        var documentFiles = IndexExecutionPlanBuilder.EnumerateIndexableFiles(
            rootPath,
            includeCSharp: false,
            includeTypeScript: false,
            includeDocs: true,
            includeConfiguration: false);

        var changedDocumentFiles = DocumentIndexingSelection.SelectDocumentationFilesForIndexing(
            documentFiles,
            rootPath,
            changedFiles);

        if (changedFiles is null)
            return new DocumentIndexRunPlan(changedDocumentFiles, []);

        var deletedDocumentFiles = DocumentIndexingSelection.FilterDocumentationRelativePaths(deletedFiles, rootPath);
        var changedDocumentRelativePaths = DocumentIndexingSelection.FilterDocumentationRelativePaths(changedFiles, rootPath);

        var filesToDelete = deletedDocumentFiles
            .Concat(changedDocumentRelativePaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new DocumentIndexRunPlan(changedDocumentFiles, filesToDelete);
    }
}
