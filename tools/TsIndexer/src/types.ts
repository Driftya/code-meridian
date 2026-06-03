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
  | 'Module';

export type CodeEdgeType =
  | 'Contains'
  | 'Calls'
  | 'Implements'
  | 'Inherits'
  | 'Uses'
  | 'DependsOn'
  | 'Overrides';

export interface CodeNodeDto {
  id: string;
  name: string;
  type: CodeNodeType;
  namespace?: string;
  filePath?: string;
  lineNumber?: number;
  lineCount?: number;
  summary?: string;
  projectContext: string;
}

export interface CodeEdgeDto {
  sourceId: string;
  targetId: string;
  type: CodeEdgeType;
}

export interface DocumentDto {
  id?: string;
  content: string;
  source: string;
  projectContext: string;
}
