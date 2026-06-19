export interface IndexerBatchFile {
    files: string[];
    fileRoles: Map<string, string>;
}
export declare function readIndexerBatchFile(rootPath: string, batchFilePath: string): IndexerBatchFile;
