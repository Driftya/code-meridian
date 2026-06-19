export interface IndexCommandOptions {
    path: string;
    project: string;
    url: string;
    batchFile: string;
}
export interface ResolvedIndexCommandOptions {
    rootPath: string;
    projectName: string;
    serverUrl: string;
    apiKey?: string;
    batchFilePath: string;
}
