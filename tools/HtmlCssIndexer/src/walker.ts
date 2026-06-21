import path from 'node:path';
import type { CodeEdgeDto, CodeNodeDto } from '#indexer-shared';
import { collectHtmlArtifacts, collectTsxArtifacts } from './walker/markup.js';
import { collectStyleArtifacts } from './walker/css.js';
import type { FrontendWalkProgress, FrontendWalkResult } from './types.js';

export function walkFrontend(
  rootPath: string,
  projectName: string,
  files: string[],
  resolveFileRole?: (relativePath: string) => string | undefined,
  onProgress?: (progress: FrontendWalkProgress) => void,
): FrontendWalkResult {
  const nodes: CodeNodeDto[] = [];
  const edges: CodeEdgeDto[] = [];
  const knownIds = new Set<string>();
  const totalFiles = files.length;

  for (let index = 0; index < files.length; index++) {
    const file = files[index];
    const extension = path.extname(file).toLowerCase();
    if (extension === '.html') {
      collectHtmlArtifacts(rootPath, projectName, file, nodes, edges, knownIds, resolveFileRole);
    } else if (extension === '.css' || extension === '.scss') {
      collectStyleArtifacts(rootPath, projectName, file, nodes, edges, knownIds, resolveFileRole);
    } else if (extension === '.tsx' || extension === '.jsx') {
      collectTsxArtifacts(rootPath, projectName, file, nodes, edges, knownIds, resolveFileRole);
    }

    onProgress?.({
      processedFiles: index + 1,
      totalFiles,
      currentFile: file,
    });
  }

  return { nodes, edges };
}
