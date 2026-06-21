export type FrontendConceptKind =
  | 'CssClass'
  | 'CssId'
  | 'CssSelector'
  | 'CssVariable'
  | 'CssDeclaration';

export interface FrontendWalkResult {
  nodes: import('#indexer-shared').CodeNodeDto[];
  edges: import('#indexer-shared').CodeEdgeDto[];
}
