import { CodeMeridianClient, readIndexerBatchFile } from '../../../IndexerShared/dist/index.js';
import { walkTypeScript } from '../walker.js';
import { indexTypeScriptDiagnostics } from '../diagnostics/type-script-diagnostics.js';
import { analyzeTypeScriptBoundaries } from '../analysis/type-script-boundaries.js';
const INGEST_CONCURRENCY = 8;
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
        const batch = readIndexerBatchFile(options.rootPath, options.batchFilePath);
        console.log(`  Batch size: ${batch.files.length} file(s)`);
        const { nodes, edges } = walkTypeScript(options.rootPath, options.projectName, batch.files, relativePath => batch.fileRoles.get(relativePath));
        console.log(`  Found ${nodes.length} nodes, ${edges.length} edges`);
        const nodeResult = await client.ingestNodes(nodes, {
            concurrency: INGEST_CONCURRENCY,
            onError: (node, error, errorCount) => {
                if (errorCount <= 5)
                    console.warn(`  warn: node ${node.id}: ${error}`);
            },
        });
        console.log(`  Ingested ${nodeResult.successCount} nodes${nodeResult.errorCount > 0 ? ` (${nodeResult.errorCount} errors)` : ''}`);
        const edgeResult = await client.ingestEdges(edges, {
            concurrency: INGEST_CONCURRENCY,
            onError: (edge, error, errorCount) => {
                if (errorCount <= 5)
                    console.warn(`  warn: edge ${edge.sourceId} -> ${edge.targetId}: ${error}`);
            },
        });
        console.log(`  Ingested ${edgeResult.successCount} edges${edgeResult.errorCount > 0 ? ` (${edgeResult.errorCount} errors)` : ''}`);
        const diagnosticsCount = await indexTypeScriptDiagnostics(client, options.rootPath, options.projectName);
        console.log(`  Indexed ${diagnosticsCount} TypeScript diagnostic(s)`);
        console.log(`\nDone. '${options.projectName}' indexed into CodeMeridian at ${options.serverUrl}`);
    }
}
