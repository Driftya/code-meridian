export type FrontendConceptKind =
  | 'CssClass'
  | 'CssId'
  | 'CssSelector'
  | 'CssVariable';

export interface FrontendWalkResult {
  nodes: import('@codemeridian/indexer-shared').CodeNodeDto[];
  edges: import('@codemeridian/indexer-shared').CodeEdgeDto[];
}
