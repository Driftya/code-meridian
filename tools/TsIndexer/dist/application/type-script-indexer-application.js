import fs from 'node:fs';
import path from 'node:path';
import { watch as chokidarWatch } from 'chokidar';
import { CodeMeridianClient } from '../client.js';
import { walkTypeScript } from '../walker.js';
import { buildTsIndexerPlan, loadTsIndexerCache, saveTsIndexerCache, } from '../storage/indexer-cache.js';
import { indexTypeScriptDiagnostics } from '../diagnostics/type-script-diagnostics.js';
import { analyzeTypeScriptBoundaries } from '../analysis/type-script-boundaries.js';
import { loadIndexedFileRoleClassifier } from '../services/file-roles.js';
import { discoverTypeScriptFiles, isDocumentationFile, isTypeScriptSourceFile, readFilesList, resolveDocumentFiles, resolveInputFile, } from '../services/project-discovery.js';
export class TypeScriptIndexerApplication {
    async run(options) {
        const client = new CodeMeridianClient(options.serverUrl, options.apiKey);
        console.log(`Storage mode: ${options.storageMode}`);
        if (options.clear) {
            process.stdout.write(`Clearing existing knowledge for '${options.projectName}'... `);
            await client.clearProject(options.projectName);
            console.log('done');
        }
        await this.runIndexPass(options, client);
        if (!options.watch) {
            return 0;
        }
        await this.runWatchLoop(options, client);
        return 0;
    }
    async runIndexPass(options, client) {
        console.log(`Walking TypeScript files in ${options.rootPath}...`);
        const boundaries = analyzeTypeScriptBoundaries(options.rootPath);
        if (boundaries.length > 0) {
            console.log(`  Detected ${boundaries.length} TypeScript project boundary/boundaries`);
        }
        const files = options.filesListPath ? readFilesList(options.filesListPath) : undefined;
        if (files) {
            console.log(`  Incremental file list: ${files.length} file(s)`);
        }
        const allTypeScriptFiles = files
            ? files.map(file => resolveInputFile(options.rootPath, file)).filter(isTypeScriptSourceFile)
            : discoverTypeScriptFiles(options.rootPath);
        const docFiles = options.includeDocs ? resolveDocumentFiles(options.rootPath, files) : [];
        logClassificationSummary(options.rootPath, options.projectName, allTypeScriptFiles, docFiles);
        const existingState = options.forceFull ? undefined : loadTsIndexerCache(options.cacheDirectory, options.projectName);
        const plan = buildTsIndexerPlan(options.rootPath, allTypeScriptFiles, existingState);
        const typeScriptFiles = files
            ? allTypeScriptFiles
            : plan.changedFiles.map(file => resolveInputFile(options.rootPath, file));
        const { nodes, edges } = walkTypeScript(options.rootPath, options.projectName, typeScriptFiles);
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
        if (options.includeDocs) {
            let docCount = 0;
            for (const docPath of docFiles) {
                const relPath = path.relative(options.rootPath, docPath).replace(/\\/g, '/');
                const content = fs.readFileSync(docPath, 'utf-8');
                if (content.trim().length === 0)
                    continue;
                await client.ingestDocument({
                    id: `${options.projectName}::doc::${relPath}`,
                    source: relPath,
                    content,
                    projectContext: options.projectName,
                });
                docCount++;
            }
            console.log(`  Ingested ${docCount} document(s)`);
        }
        if (plan.deletedFiles.length > 0) {
            for (const deletedPath of plan.deletedFiles) {
                await client.deleteProjectFile(options.projectName, deletedPath).catch(() => { });
            }
            console.log(`  Removed ${plan.deletedFiles.length} deleted file(s) from the project graph`);
        }
        const diagnosticsCount = await indexTypeScriptDiagnostics(client, options.rootPath, options.projectName);
        console.log(`  Indexed ${diagnosticsCount} TypeScript diagnostic(s)`);
        saveTsIndexerCache(options.cacheDirectory, options.projectName, plan.nextState);
        console.log(`\nDone. '${options.projectName}' indexed into CodeMeridian at ${options.serverUrl}`);
    }
    async runWatchLoop(options, client) {
        console.log(`\nWatch mode active - monitoring ${options.rootPath} for .ts/.tsx/.md changes. Press Ctrl+C to exit.\n`);
        let debounceTimer;
        const changed = new Set();
        const deleted = new Set();
        const reindex = (filePath, wasDeleted) => {
            const relativePath = path.relative(options.rootPath, filePath).replace(/\\/g, '/');
            if (wasDeleted)
                deleted.add(relativePath);
            else
                changed.add(filePath);
            clearTimeout(debounceTimer);
            debounceTimer = setTimeout(async () => {
                console.log('[watch] Change detected - re-indexing...');
                const changedBatch = Array.from(changed);
                const deletedBatch = Array.from(deleted);
                changed.clear();
                deleted.clear();
                try {
                    const removedPaths = [
                        ...changedBatch.map(file => path.relative(options.rootPath, file).replace(/\\/g, '/')),
                        ...deletedBatch,
                    ];
                    for (const relPath of removedPaths) {
                        await client.deleteProjectFile(options.projectName, relPath).catch(() => { });
                    }
                    const changedTsFiles = changedBatch.filter(isTypeScriptSourceFile);
                    const result = changedTsFiles.length > 0
                        ? walkTypeScript(options.rootPath, options.projectName, changedTsFiles)
                        : { nodes: [], edges: [] };
                    for (const node of result.nodes) {
                        await client.ingestNode(node).catch(() => { });
                    }
                    for (const edge of result.edges) {
                        await client.ingestEdge(edge).catch(() => { });
                    }
                    const diagnosticsCount = await indexTypeScriptDiagnostics(client, options.rootPath, options.projectName).catch(() => 0);
                    let docCount = 0;
                    if (options.includeDocs) {
                        for (const docPath of changedBatch.filter(file => isDocumentationFile(file) && fs.existsSync(file))) {
                            const relPath = path.relative(options.rootPath, docPath).replace(/\\/g, '/');
                            const content = fs.readFileSync(docPath, 'utf-8');
                            if (content.trim().length === 0)
                                continue;
                            await client.ingestDocument({
                                id: `${options.projectName}::doc::${relPath}`,
                                source: relPath,
                                content,
                                projectContext: options.projectName,
                            }).catch(() => { });
                            docCount++;
                        }
                    }
                    console.log(`[watch] Done: ${result.nodes.length} nodes, ${result.edges.length} edges, ${docCount} docs, ${diagnosticsCount} diagnostics`);
                }
                catch (error) {
                    console.error('[watch] Re-index failed:', error);
                }
            }, 2_000);
        };
        chokidarWatch(options.rootPath, {
            ignored: [
                /(^|[/\\])\../,
                /node_modules/,
                /dist/,
                /build/,
                /\.d\.ts$/,
            ],
            persistent: true,
            ignoreInitial: true,
        })
            .on('add', filePath => reindex(filePath, false))
            .on('change', filePath => reindex(filePath, false))
            .on('unlink', filePath => reindex(filePath, true));
        await new Promise((resolve) => {
            process.on('SIGINT', () => {
                console.log('\nWatch mode stopped.');
                resolve();
            });
            process.on('SIGTERM', () => resolve());
        });
    }
}
function logClassificationSummary(rootPath, projectName, sourceFiles, docFiles) {
    const classifyFileRole = loadIndexedFileRoleClassifier(rootPath);
    const counts = new Map();
    const files = [...sourceFiles, ...docFiles];
    for (const file of files) {
        const relativePath = path.relative(rootPath, file).replace(/\\/g, '/');
        const role = classifyFileRole(relativePath);
        counts.set(role, (counts.get(role) ?? 0) + 1);
    }
    console.log(`  Classified ${files.length} indexed files for ${projectName}: ` +
        `Source=${counts.get('Source') ?? 0}, ` +
        `Test=${counts.get('Test') ?? 0}, ` +
        `Migration=${counts.get('Migration') ?? 0}, ` +
        `Snapshot=${counts.get('Snapshot') ?? 0}, ` +
        `Generated=${counts.get('Generated') ?? 0}, ` +
        `BuildArtifact=${counts.get('BuildArtifact') ?? 0}, ` +
        `Documentation=${counts.get('Documentation') ?? 0}, ` +
        `Configuration=${counts.get('Configuration') ?? 0}, ` +
        `Unknown=${counts.get('Unknown') ?? 0}`);
}
