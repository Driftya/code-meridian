import { createHash } from 'node:crypto';
import path from 'node:path';
import { CodeMeridianClient, readIndexerBatchFile } from '../../../IndexerShared/dist/index.js';
import { walkTypeScript } from '../walker.js';
import { analyzeTypeScriptBoundaries } from '../analysis/type-script-boundaries.js';
const INGEST_CONCURRENCY = 8;
export class TypeScriptIndexerApplication {
    async run(options, report) {
        const client = new CodeMeridianClient(options.serverUrl, options.apiKey);
        return await this.runIndexPass(options, client, report);
    }
    async runIndexPass(options, client, report) {
        const workspaceRootPath = options.workspaceRootPath ?? options.rootPath;
        console.log(`Indexing TypeScript/JavaScript batch in ${workspaceRootPath}...`);
        const boundaries = analyzeTypeScriptBoundaries(workspaceRootPath);
        if (boundaries.length > 0) {
            console.log(`  Detected ${boundaries.length} TypeScript project boundary/boundaries`);
        }
        const batch = readIndexerBatchFile(options.rootPath, options.batchFilePath);
        report?.capture(batch.files);
        console.log(`  Batch size: ${batch.files.length} file(s)`);
        const { nodes, edges, relationshipHealth, usedFullResolutionCatalog, resolutionCatalog, fileOutcomes } = walkTypeScript(options.rootPath, options.projectName, batch.files, relativePath => batch.fileRoles.get(relativePath), undefined, true, workspaceRootPath, report?.read);
        if (report)
            for (const file of fileOutcomes)
                for (const diagnostic of file.diagnostics)
                    report.mark(file.file, file.status, diagnostic);
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
        if (nodeResult.errorCount > 0 || nodeResult.successCount !== nodes.length)
            return 1;
        const edgeResult = await client.ingestEdges(edges, {
            concurrency: INGEST_CONCURRENCY,
            onError: (edge, error, errorCount) => {
                if (errorCount <= 5)
                    console.warn(`  warn: edge ${edge.sourceId} -> ${edge.targetId}: ${error}`);
            },
        });
        console.log(`  Ingested ${edgeResult.successCount} edges${edgeResult.errorCount > 0 ? ` (${edgeResult.errorCount} errors)` : ''}`);
        if (edgeResult.errorCount > 0 || edgeResult.successCount !== edges.length)
            return 1;
        await persistIndexRun(client, options, batch.files.length, nodeResult.successCount, edgeResult.successCount, edges, relationshipHealth, resolutionCatalog.reason === undefined && (!options.isIncremental || usedFullResolutionCatalog), resolutionCatalog);
        const partial = fileOutcomes.some(file => file.status !== 'complete') || !!resolutionCatalog.reason
            || relationshipHealth.calls.unresolvedLocal + relationshipHealth.calls.indeterminate
                + relationshipHealth.typeReferences.unresolvedLocal + relationshipHealth.typeReferences.indeterminate > 0;
        if (report) {
            report.published = true;
            report.capabilities.callable_definitions = partial ? 'partial' : 'unknown';
            report.capabilities.callable_dependencies = partial ? 'partial' : 'unknown';
            report.limitations.push(`Resolution catalog: ${resolutionCatalog.completeness}; selected ${batch.files.length}; loaded ${fileOutcomes.length}.`, 'Callable representation, imported catalog source binding and owned replacement remain unaudited; clean execution does not grant callable readiness.');
            if (partial)
                report.limitations.push('Compiler, root-loading or unresolved/indeterminate relationship evidence is incomplete.');
        }
        console.log(`\nDone. '${options.projectName}' indexed into CodeMeridian at ${options.serverUrl}`);
        return partial ? 2 : 0;
    }
}
async function persistIndexRun(client, options, scannedFileCount, ingestedNodeCount, ingestedEdgeCount, edges, health, usedFullResolutionCatalog, resolutionCatalog) {
    const mode = options.isIncremental ? 'incremental' : 'full';
    const normalizedScope = path.resolve(options.workspaceRootPath ?? options.rootPath).replace(/\\/g, '/');
    const scopeId = createHash('sha256').update(normalizedScope.toLowerCase()).digest('hex').slice(0, 16);
    const calls = health.calls;
    const references = health.typeReferences;
    const properties = {
        externalKind: 'IndexRun',
        relationshipHealthSchemaVersion: '2',
        edgeEvidenceCounts: JSON.stringify(edges.reduce((counts, edge) => {
            const kind = edge.evidenceKind ?? 'unknown';
            counts[kind] = (counts[kind] ?? 0) + 1;
            return counts;
        }, {})),
        edgeEvidenceGroups: JSON.stringify(edges.reduce((groups, edge) => {
            const key = [edge.evidenceKind ?? 'unknown', edge.resolver ?? 'unknown',
                edge.evidenceReason ?? 'unknown', edge.evidenceDetails?.fileRole ?? 'Unknown'].join('|');
            groups[key] = (groups[key] ?? 0) + 1;
            return groups;
        }, {})),
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
