import fs from 'node:fs';
import path from 'node:path';
import { Project } from 'ts-morph';
import { collectConfigurationEdges, collectConfigurationNodes } from './walker/configuration.js';
import { collectDatabaseTracingEdges, collectDatabaseTracingNodes } from './walker/database-tracing.js';
import { loadDatabaseTracingOptions } from './walker/database-tracing-options.js';
import { collectEdges, collectNodes } from './walker/graph.js';
import { collectRouteEdges, collectRouteNodes } from './walker/routes.js';
import { discoverTypeScriptFiles } from './services/project-discovery.js';
import { RelationshipOutcomeCollector, } from './relationship-health.js';
export function walkTypeScript(rootPath, projectName, files, resolveFileRole, databaseTracingOptions, loadFullResolutionCatalog = false) {
    const nodes = [];
    const edges = [];
    const catalogNodes = [];
    const catalogKnownIds = new Set();
    const methodIndex = new Map();
    const callOutcomes = new RelationshipOutcomeCollector('Calls');
    const typeReferenceOutcomes = new RelationshipOutcomeCollector('TypeReferences');
    const tracingOptions = databaseTracingOptions ?? loadDatabaseTracingOptions(rootPath);
    const catalogStartedAt = performance.now();
    const tsConfigPath = path.join(rootPath, 'tsconfig.json');
    let catalogReason;
    let tsProject;
    try {
        tsProject = createProject(tsConfigPath);
    }
    catch {
        catalogReason = 'tsconfig_load_failed';
        tsProject = createProject();
    }
    let resolutionFiles = files;
    if (loadFullResolutionCatalog) {
        try {
            resolutionFiles = discoverTypeScriptFiles(rootPath);
        }
        catch {
            catalogReason ??= 'project_discovery_failed';
        }
    }
    const projectFiles = [...new Set([...resolutionFiles, ...files].map(normalizeFilePath))];
    try {
        if (projectFiles.length > 0) {
            tsProject.addSourceFilesAtPaths(projectFiles);
        }
    }
    catch {
        catalogReason ??= 'resolution_catalog_load_failed';
        tsProject = createProject();
        tsProject.addSourceFilesAtPaths([...new Set(files.map(normalizeFilePath))]);
    }
    const sourceFiles = tsProject.getSourceFiles();
    const emittedFilePaths = new Set(files.map(file => normalizeFilePath(file).toLowerCase()));
    const emittedSourceFiles = sourceFiles.filter(sourceFile => emittedFilePaths.has(normalizeFilePath(sourceFile.getFilePath()).toLowerCase()));
    for (const sourceFile of sourceFiles) {
        collectNodes(sourceFile, rootPath, projectName, catalogNodes, catalogKnownIds, resolveFileRole);
    }
    for (const sourceFile of sourceFiles) {
        collectRouteNodes(sourceFile, rootPath, projectName, catalogNodes, catalogKnownIds, resolveFileRole);
    }
    for (const sourceFile of sourceFiles) {
        collectConfigurationNodes(sourceFile, rootPath, projectName, catalogNodes, catalogKnownIds, resolveFileRole);
    }
    for (const sourceFile of sourceFiles) {
        collectDatabaseTracingNodes(sourceFile, rootPath, projectName, catalogNodes, catalogKnownIds, tracingOptions, resolveFileRole);
    }
    indexMethods(catalogNodes, methodIndex);
    const emittedKnownIds = new Set();
    for (const sourceFile of emittedSourceFiles) {
        collectNodes(sourceFile, rootPath, projectName, nodes, emittedKnownIds, resolveFileRole);
        collectRouteNodes(sourceFile, rootPath, projectName, nodes, emittedKnownIds, resolveFileRole);
        collectConfigurationNodes(sourceFile, rootPath, projectName, nodes, emittedKnownIds, resolveFileRole);
        collectDatabaseTracingNodes(sourceFile, rootPath, projectName, nodes, emittedKnownIds, tracingOptions, resolveFileRole);
    }
    for (const sourceFile of emittedSourceFiles) {
        collectEdges(sourceFile, rootPath, projectName, catalogNodes, edges, catalogKnownIds, methodIndex, callOutcomes, typeReferenceOutcomes);
        collectRouteEdges(sourceFile, rootPath, projectName, edges, catalogKnownIds);
        collectConfigurationEdges(sourceFile, rootPath, projectName, edges);
        collectDatabaseTracingEdges(sourceFile, rootPath, projectName, edges, tracingOptions);
    }
    return {
        nodes,
        edges,
        relationshipHealth: {
            schemaVersion: 2,
            calls: callOutcomes.build(),
            typeReferences: typeReferenceOutcomes.build(),
        },
        usedFullResolutionCatalog: loadFullResolutionCatalog && catalogReason === undefined,
        resolutionCatalog: {
            completeness: catalogReason !== undefined
                ? 'partial'
                : loadFullResolutionCatalog ? 'full' : 'batch',
            ...(catalogReason !== undefined ? { reason: catalogReason } : {}),
            sourceFileCount: sourceFiles.length,
            loadDurationMs: Math.max(0, Math.round(performance.now() - catalogStartedAt)),
            heapUsedBytes: process.memoryUsage().heapUsed,
        },
    };
}
function createProject(tsConfigPath) {
    return new Project({
        ...(tsConfigPath && fs.existsSync(tsConfigPath) ? { tsConfigFilePath: tsConfigPath } : {}),
        skipAddingFilesFromTsConfig: true,
        skipFileDependencyResolution: true,
    });
}
function normalizeFilePath(filePath) {
    return path.resolve(filePath).replace(/\\/g, '/');
}
function indexMethods(nodes, methodIndex) {
    for (const node of nodes) {
        if (node.type !== 'Method')
            continue;
        const shortName = methodShortName(node.name);
        const ids = methodIndex.get(shortName) ?? [];
        ids.push(node.id);
        methodIndex.set(shortName, ids);
    }
}
function methodShortName(name) {
    const withoutParams = name.split('(')[0];
    const segments = withoutParams.split('.');
    return segments[segments.length - 1] ?? withoutParams;
}
