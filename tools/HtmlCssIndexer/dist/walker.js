import path from 'node:path';
import { collectHtmlArtifacts, collectTsxArtifacts } from './walker/markup.js';
import { collectStyleArtifacts } from './walker/css.js';
export function walkFrontend(rootPath, projectName, files, resolveFileRole, onProgress) {
    const nodes = [];
    const edges = [];
    const knownIds = new Set();
    const totalFiles = files.length;
    for (let index = 0; index < files.length; index++) {
        const file = files[index];
        const extension = path.extname(file).toLowerCase();
        if (extension === '.html') {
            collectHtmlArtifacts(rootPath, projectName, file, nodes, edges, knownIds, resolveFileRole);
        }
        else if (extension === '.css' || extension === '.scss') {
            collectStyleArtifacts(rootPath, projectName, file, nodes, edges, knownIds, resolveFileRole);
        }
        else if (extension === '.tsx' || extension === '.jsx') {
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
