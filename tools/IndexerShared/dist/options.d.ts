export interface IndexCommandOptions {
    path: string;
    project: string;
    url: string;
    batchFile: string;
    incremental?: boolean;
}
export interface ResolvedIndexCommandOptions {
    rootPath: string;
    projectName: string;
    serverUrl: string;
    apiKey?: string;
    batchFilePath: string;
    isIncremental?: boolean;
}
