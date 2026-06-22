import fs from 'node:fs';
import path from 'node:path';
import { Project } from 'ts-morph';
import { collectConfigurationEdges, collectConfigurationNodes } from './walker/configuration.js';
import { collectDatabaseTracingEdges, collectDatabaseTracingNodes } from './walker/database-tracing.js';
import { loadDatabaseTracingOptions } from './walker/database-tracing-options.js';
import { collectEdges, collectNodes } from './walker/graph.js';
import { collectRouteEdges, collectRouteNodes } from './walker/routes.js';
export function walkTypeScript(rootPath, projectName, files, resolveFileRole, databaseTracingOptions) {
    const nodes = [];
    const edges = [];
    const knownIds = new Set();
    const methodIndex = new Map();
    const tracingOptions = databaseTracingOptions ?? loadDatabaseTracingOptions(rootPath);
    const tsConfigPath = path.join(rootPath, 'tsconfig.json');
    const tsProject = new Project({
        ...(fs.existsSync(tsConfigPath) ? { tsConfigFilePath: tsConfigPath } : {}),
        skipAddingFilesFromTsConfig: true,
        skipFileDependencyResolution: true,
    });
    if (files.length > 0) {
        tsProject.addSourceFilesAtPaths(files.map(file => path.resolve(file).replace(/\\/g, '/')));
    }
    const sourceFiles = tsProject.getSourceFiles();
    for (const sourceFile of sourceFiles) {
        collectNodes(sourceFile, rootPath, projectName, nodes, knownIds, resolveFileRole);
    }
    for (const sourceFile of sourceFiles) {
        collectRouteNodes(sourceFile, rootPath, projectName, nodes, knownIds, resolveFileRole);
    }
    for (const sourceFile of sourceFiles) {
        collectConfigurationNodes(sourceFile, rootPath, projectName, nodes, knownIds, resolveFileRole);
    }
    for (const sourceFile of sourceFiles) {
        collectDatabaseTracingNodes(sourceFile, rootPath, projectName, nodes, knownIds, tracingOptions, resolveFileRole);
    }
    indexMethods(nodes, methodIndex);
    for (const sourceFile of sourceFiles) {
        collectEdges(sourceFile, rootPath, projectName, nodes, edges, knownIds, methodIndex);
        collectRouteEdges(sourceFile, rootPath, projectName, edges, knownIds);
        collectConfigurationEdges(sourceFile, rootPath, projectName, edges);
        collectDatabaseTracingEdges(sourceFile, rootPath, projectName, edges, tracingOptions);
    }
    return { nodes, edges };
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
