import path from 'node:path';
import { CodeMeridianClient } from '../client.js';
import { walkTypeScript } from '../walker.js';
import { indexTypeScriptDiagnostics } from '../diagnostics/type-script-diagnostics.js';
import { analyzeTypeScriptBoundaries } from '../analysis/type-script-boundaries.js';
import fs from 'node:fs';
export class TypeScriptIndexerApplication {
    async run(options) {
        const client = new CodeMeridianClient(options.serverUrl, options.apiKey);
        await this.runIndexPass(options, client);
        return 0;
    }
    async runIndexPass(options, client) {
        console.log(`Indexing TypeScript batch in ${options.rootPath}...`);
        const boundaries = analyzeTypeScriptBoundaries(options.rootPath);
        if (boundaries.length > 0) {
            console.log(`  Detected ${boundaries.length} TypeScript project boundary/boundaries`);
        }
        const batch = readBatchFile(options.rootPath, options.batchFilePath);
        console.log(`  Batch size: ${batch.files.length} file(s)`);
        const { nodes, edges } = walkTypeScript(options.rootPath, options.projectName, batch.files, relativePath => batch.fileRoles.get(relativePath));
        console.log(`  Found ${nodes.length} nodes, ${edges.length} edges`);
        let nodeCount = 0;
        let nodeErrors = 0;
        for (const node of nodes) {
            try {
                await client.ingestNode(node);
                nodeCount++;
            }
            catch (error) {
                nodeErrors++;
                if (nodeErrors <= 5)
                    console.warn(`  warn: node ${node.id}: ${error}`);
            }
        }
        console.log(`  Ingested ${nodeCount} nodes${nodeErrors > 0 ? ` (${nodeErrors} errors)` : ''}`);
        let edgeCount = 0;
        let edgeErrors = 0;
        for (const edge of edges) {
            try {
                await client.ingestEdge(edge);
                edgeCount++;
            }
            catch (error) {
                edgeErrors++;
                if (edgeErrors <= 5)
                    console.warn(`  warn: edge ${edge.sourceId} -> ${edge.targetId}: ${error}`);
            }
        }
        console.log(`  Ingested ${edgeCount} edges${edgeErrors > 0 ? ` (${edgeErrors} errors)` : ''}`);
        const diagnosticsCount = await indexTypeScriptDiagnostics(client, options.rootPath, options.projectName);
        console.log(`  Indexed ${diagnosticsCount} TypeScript diagnostic(s)`);
        console.log(`\nDone. '${options.projectName}' indexed into CodeMeridian at ${options.serverUrl}`);
    }
}
function readBatchFile(rootPath, batchFilePath) {
    const payload = JSON.parse(fs.readFileSync(batchFilePath, 'utf8'));
    const files = [];
    const fileRoles = new Map();
    for (const entry of payload) {
        const normalizedEntryPath = entry.path.replace(/[\\/]/g, path.sep);
        const fullPath = path.resolve(rootPath, normalizedEntryPath);
        files.push(fullPath);
        if (entry.fileRole) {
            const relativePath = path.relative(rootPath, fullPath).replace(/\\/g, '/');
            fileRoles.set(relativePath, entry.fileRole);
        }
    }
    return { files, fileRoles };
}
