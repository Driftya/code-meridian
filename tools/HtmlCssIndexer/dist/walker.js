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
        const firstEdge = edges.length;
        const relativePath = path.relative(rootPath, file).replace(/\\/g, '/');
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
        for (const edge of edges.slice(firstEdge)) {
            const location = edge.callSite?.match(/^(.*):(\d+)$/);
            edge.evidenceKind ??= edge.type === 'Overrides' ? 'inferred' : 'extracted';
            edge.evidenceReason ??= edge.type === 'Overrides' ? 'cascade_order' : 'frontend_syntax';
            edge.resolver ??= 'frontend.parser';
            edge.sourceFilePath ??= location?.[1] ?? relativePath;
            edge.sourceLine ??= location ? Number(location[2]) : undefined;
            const fileRole = resolveFileRole?.(relativePath);
            if (fileRole)
                edge.evidenceDetails = { ...edge.evidenceDetails, fileRole };
        }
        onProgress?.({
            processedFiles: index + 1,
            totalFiles,
            currentFile: file,
        });
    }
    return { nodes, edges };
}
