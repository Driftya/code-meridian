export interface IndexCommandOptions {
    path: string;
    workspaceRoot?: string;
    project: string;
    url: string;
    batchFile: string;
    incremental?: boolean;
}
export interface ResolvedIndexCommandOptions {
    rootPath: string;
    workspaceRootPath?: string;
    projectName: string;
    serverUrl: string;
    apiKey?: string;
    batchFilePath: string;
    isIncremental?: boolean;
}
