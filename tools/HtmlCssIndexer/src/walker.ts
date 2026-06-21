import path from 'node:path';
import type { CodeEdgeDto, CodeNodeDto } from '#indexer-shared';
import { collectHtmlArtifacts, collectTsxArtifacts } from './walker/markup.js';
import { collectStyleArtifacts } from './walker/css.js';
import type { FrontendWalkResult } from './types.js';

export function walkFrontend(
  rootPath: string,
  projectName: string,
  files: string[],
  resolveFileRole?: (relativePath: string) => string | undefined,
): FrontendWalkResult {
  const nodes: CodeNodeDto[] = [];
  const edges: CodeEdgeDto[] = [];
  const knownIds = new Set<string>();

  for (const file of files) {
    const extension = path.extname(file).toLowerCase();
    if (extension === '.html') {
      collectHtmlArtifacts(rootPath, projectName, file, nodes, edges, knownIds, resolveFileRole);
      continue;
    }

    if (extension === '.css' || extension === '.scss') {
      collectStyleArtifacts(rootPath, projectName, file, nodes, edges, knownIds, resolveFileRole);
      continue;
    }

    if (extension === '.tsx' || extension === '.jsx') {
      collectTsxArtifacts(rootPath, projectName, file, nodes, edges, knownIds, resolveFileRole);
    }
  }

  return { nodes, edges };
}
