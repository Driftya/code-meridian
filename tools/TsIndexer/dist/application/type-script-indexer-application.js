import { createHash } from 'node:crypto';
import path from 'node:path';
import { CodeMeridianClient, readIndexerBatchFile } from '../../../IndexerShared/dist/index.js';
import { walkTypeScript } from '../walker.js';
import { analyzeTypeScriptBoundaries } from '../analysis/type-script-boundaries.js';
const INGEST_CONCURRENCY = 8;
export class TypeScriptIndexerApplication {
    async run(options) {
        const client = new CodeMeridianClient(options.serverUrl, options.apiKey);
        await this.runIndexPass(options, client);
        return 0;
    }
    async runIndexPass(options, client) {
        const workspaceRootPath = options.workspaceRootPath ?? options.rootPath;
        console.log(`Indexing TypeScript/JavaScript batch in ${workspaceRootPath}...`);
        const boundaries = analyzeTypeScriptBoundaries(workspaceRootPath);
        if (boundaries.length > 0) {
            console.log(`  Detected ${boundaries.length} TypeScript project boundary/boundaries`);
        }
        const batch = readIndexerBatchFile(options.rootPath, options.batchFilePath);
        console.log(`  Batch size: ${batch.files.length} file(s)`);
        const { nodes, edges, relationshipHealth, usedFullResolutionCatalog, resolutionCatalog } = walkTypeScript(options.rootPath, options.projectName, batch.files, relativePath => batch.fileRoles.get(relativePath), undefined, true, workspaceRootPath);
        console.log(`  Found ${nodes.length} nodes, ${edges.length} edges`);
        console.log(`  Resolution catalog: ${resolutionCatalog.completeness}, `
            + `${resolutionCatalog.sourceFileCount} source file(s), `
            + `${resolutionCatalog.loadDurationMs} ms, ${resolutionCatalog.heapUsedBytes} heap bytes`);
        if (resolutionCatalog.reason) {
            console.warn(`  Resolution catalog fallback: ${resolutionCatalog.reason}`);
        }
        console.log(`  Relationship outcomes: ${formatOutcomes('calls', relationshipHealth.calls)}; `
            + `${formatOutcomes('type references', relationshipHealth.typeReferences)}`);
        const relationshipSamples = [
            ...relationshipHealth.calls.samples,
            ...relationshipHealth.typeReferences.samples,
        ];
        if (relationshipSamples.length > 0) {
            console.warn(`  Relationship failure samples: ${relationshipSamples.map(sample => `${sample.filePath ?? sample.sourceId}:${sample.lineNumber ?? '?'} `
                + `${sample.targetName ?? 'unknown'} (${sample.reason})`).join('; ')}`);
        }
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
        await persistIndexRun(client, options, batch.files.length, nodeResult.successCount, edgeResult.successCount, relationshipHealth, resolutionCatalog.reason === undefined && (!options.isIncremental || usedFullResolutionCatalog), resolutionCatalog);
        console.log(`\nDone. '${options.projectName}' indexed into CodeMeridian at ${options.serverUrl}`);
    }
}
async function persistIndexRun(client, options, scannedFileCount, ingestedNodeCount, ingestedEdgeCount, health, usedFullResolutionCatalog, resolutionCatalog) {
    const mode = options.isIncremental ? 'incremental' : 'full';
    const normalizedScope = path.resolve(options.workspaceRootPath ?? options.rootPath).replace(/\\/g, '/');
    const scopeId = createHash('sha256').update(normalizedScope.toLowerCase()).digest('hex').slice(0, 16);
    const calls = health.calls;
    const references = health.typeReferences;
    const properties = {
        externalKind: 'IndexRun',
        relationshipHealthSchemaVersion: '2',
        language: 'TypeScript',
        resolutionScope: normalizedScope,
        mode,
        completedAt: new Date().toISOString(),
        scannedFileCount: scannedFileCount.toString(),
        ingestedFileCount: scannedFileCount.toString(),
        ingestedNodeCount: ingestedNodeCount.toString(),
        ingestedEdgeCount: ingestedEdgeCount.toString(),
        attemptedCallEdges: calls.attempted.toString(),
        resolvedCallEdges: calls.uniqueResolvedEdges.toString(),
        attemptedReferenceEdges: references.attempted.toString(),
        resolvedReferenceEdges: references.uniqueResolvedEdges.toString(),
        uniqueCallEdges: calls.uniqueResolvedEdges.toString(),
        uniqueReferenceEdges: references.uniqueResolvedEdges.toString(),
        callRelationshipOutcomes: JSON.stringify(calls),
        referenceRelationshipOutcomes: JSON.stringify(references),
        externalOrUnindexedRelationshipCount: (calls.externalOrUnindexed + references.externalOrUnindexed).toString(),
        unresolvedLocalRelationshipCount: (calls.unresolvedLocal + references.unresolvedLocal).toString(),
        indeterminateRelationshipCount: (calls.indeterminate + references.indeterminate).toString(),
        duplicateRelationshipCount: (calls.duplicateEdges + references.duplicateEdges).toString(),
        syntheticRelationshipCount: '0',
        relationshipFailureSamples: JSON.stringify([...calls.samples, ...references.samples]),
        usedFullResolutionCatalog: usedFullResolutionCatalog.toString(),
        resolutionCatalogCompleteness: usedFullResolutionCatalog ? 'full' : 'partial',
        resolutionCatalogFileCount: resolutionCatalog.sourceFileCount.toString(),
        resolutionCatalogLoadDurationMs: resolutionCatalog.loadDurationMs.toString(),
        resolutionCatalogHeapUsedBytes: resolutionCatalog.heapUsedBytes.toString(),
    };
    if (resolutionCatalog.reason) {
        properties.resolutionCatalogReason = resolutionCatalog.reason;
    }
    await client.ingestNode({
        id: `${options.projectName}::IndexRun::typescript::${scopeId}::${mode}`,
        name: `${mode} TypeScript index run`,
        type: 'Diagnostic',
        summary: `Scanned ${scannedFileCount} file(s) and classified ${calls.attempted + references.attempted} relationship candidate(s).`,
        projectContext: options.projectName,
        properties,
    });
}
function formatOutcomes(label, outcomes) {
    return `${label} attempted=${outcomes.attempted}, resolved-local=${outcomes.resolvedLocal}, `
        + `external-or-unindexed=${outcomes.externalOrUnindexed}, unresolved-local=${outcomes.unresolvedLocal}, `
        + `indeterminate=${outcomes.indeterminate}, duplicates=${outcomes.duplicateEdges}`;
}
