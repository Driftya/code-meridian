// Mirror of the C# enums in CodeMeridian.Core.CodeGraph

export type CodeNodeType =
  | 'Namespace'
  | 'Class'
  | 'Interface'
  | 'Method'
  | 'Property'
  | 'Field'
  | 'Enum'
  | 'File'
  | 'Module'
  | 'ExternalConcept'
  | 'DatabaseTable'
  | 'ApiEndpoint'
  | 'MessageTopic'
  | 'ExternalService'
  | 'Diagnostic';

export type CodeEdgeType =
  | 'Contains'
  | 'Calls'
  | 'Implements'
  | 'Inherits'
  | 'Uses'
  | 'DependsOn'
  | 'Overrides'
  | 'Reads'
  | 'Writes'
  | 'PublishesTo'
  | 'SubscribesTo';

export interface CodeNodeDto {
  id: string;
  name: string;
  type: CodeNodeType;
  namespace?: string;
  filePath?: string;
  lineNumber?: number;
  lineCount?: number;
  summary?: string;
  sourceSnippet?: string;
  sourceHash?: string;
  projectContext: string;
  embeddingCsv?: string;
}

export interface CodeEdgeDto {
  sourceId: string;
  targetId: string;
  type: CodeEdgeType;
  isAsync?: boolean;
  callSite?: string;
  paramCount?: number;
  confidence?: number;
}

export interface DocumentDto {
  id?: string;
  content: string;
  source: string;
  projectContext: string;
}
