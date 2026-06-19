import path from 'node:path';
import { collectHtmlArtifacts, collectTsxArtifacts } from './walker/markup.js';
import { collectStyleArtifacts } from './walker/css.js';
export function walkFrontend(rootPath, projectName, files, resolveFileRole) {
    const nodes = [];
    const edges = [];
    const knownIds = new Set();
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
