export interface IndexCommandOptions {
  path: string;
  project?: string;
  url?: string;
  clear: boolean;
  includeDocs: boolean;
  watch: boolean;
  filesList?: string;
}

export interface ResolvedIndexCommandOptions {
  rootPath: string;
  projectName: string;
  serverUrl: string;
  apiKey?: string;
  clear: boolean;
  includeDocs: boolean;
  watch: boolean;
  filesListPath?: string;
  storageMode: 'repo' | 'global';
  cacheDirectory: string;
}
