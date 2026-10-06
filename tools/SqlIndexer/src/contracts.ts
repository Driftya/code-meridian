import type { CodeEdgeDto, CodeNodeDto } from '../../IndexerShared/dist/index.js';

export interface SqlSettings {
  enabled: boolean;
  defaultDialect: string;
  databaseScope: string;
  searchPath?: string[];
  sources?: { pattern: string; dialect: string; databaseScope?: string }[];
}

export interface SqlSource {
  path: string;
  text: string;
  scope: string;
  fileRole?: string;
}

export interface SqlName { schema?: string; name: string }
export type SqlObjectKind = 'DatabaseTable' | 'DatabaseView' | 'DatabaseColumn' | 'DatabaseFunction';

export interface SqlDeclaration {
  name: SqlName;
  kind: SqlObjectKind;
  path: string;
  scope: string;
  offset: number;
  signature?: string;
  parent?: SqlName;
}

export interface SqlReference {
  name: SqlName;
  operation: 'Reads' | 'Writes' | 'Alters' | 'References' | 'JoinsWith';
  offset: number;
  owner?: SqlDeclaration;
  other?: SqlName;
  properties?: Record<string, string>;
  reason?: string;
}

export interface SqlFileFacts {
  source: SqlSource;
  declarations: SqlDeclaration[];
  references: SqlReference[];
  diagnostics: string[];
  status: 'complete' | 'partial' | 'failed';
}

export interface SqlAnalysisResult {
  contractVersion: '1';
  dialect: string;
  files: SqlFileFacts[];
  parser: string;
  grammarVersion: number;
}

export interface SqlDialectAnalyzer {
  readonly dialect: string;
  readonly versionFingerprint: string;
  readonly capabilities: readonly string[];
  analyze(sources: SqlSource[], signal?: AbortSignal): Promise<SqlAnalysisResult>;
}

export interface SqlFileGraph {
  path: string;
  status: 'complete' | 'partial' | 'failed';
  diagnostics: string[];
  nodes: CodeNodeDto[];
  edges: CodeEdgeDto[];
}

export interface SqlGraphSnapshot {
  contractVersion: '1';
  projectContext: string;
  files: SqlFileGraph[];
}
