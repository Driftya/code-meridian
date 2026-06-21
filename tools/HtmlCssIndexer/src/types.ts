export type FrontendConceptKind =
  | 'CssClass'
  | 'CssId'
  | 'CssSelector'
  | 'CssVariable'
  | 'CssDeclaration';

export interface FrontendWalkResult {
  nodes: import('../../IndexerShared/dist/types.js').CodeNodeDto[];
  edges: import('../../IndexerShared/dist/types.js').CodeEdgeDto[];
}

export interface FrontendWalkProgress {
  processedFiles: number;
  totalFiles: number;
  currentFile: string;
}
