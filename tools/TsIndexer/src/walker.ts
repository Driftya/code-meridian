import fs from 'node:fs';
import path from 'node:path';
import { Project } from 'ts-morph';
import type { CodeEdgeDto, CodeNodeDto } from './types.js';
import { collectConfigurationEdges, collectConfigurationNodes } from './walker/configuration.js';
import { collectDatabaseTracingEdges, collectDatabaseTracingNodes } from './walker/database-tracing.js';
import { loadDatabaseTracingOptions, type DatabaseTracingOptions } from './walker/database-tracing-options.js';
import { collectEdges, collectNodes } from './walker/graph.js';
import { collectRouteEdges, collectRouteNodes } from './walker/routes.js';
import { discoverTypeScriptFiles } from './services/project-discovery.js';
import {
  RelationshipOutcomeCollector,
  type TypeScriptRelationshipHealth,
} from './relationship-health.js';

export interface WalkResult {
  nodes: CodeNodeDto[];
  edges: CodeEdgeDto[];
  relationshipHealth: TypeScriptRelationshipHealth;
  usedFullResolutionCatalog: boolean;
}

export function walkTypeScript(
  rootPath: string,
  projectName: string,
  files: string[],
  resolveFileRole?: (relativePath: string) => string | undefined,
  databaseTracingOptions?: DatabaseTracingOptions,
  loadFullResolutionCatalog = false,
): WalkResult {
  const nodes: CodeNodeDto[] = [];
  const edges: CodeEdgeDto[] = [];
  const catalogNodes: CodeNodeDto[] = [];
  const catalogKnownIds = new Set<string>();
  const methodIndex = new Map<string, string[]>();
  const callOutcomes = new RelationshipOutcomeCollector('Calls');
  const typeReferenceOutcomes = new RelationshipOutcomeCollector('TypeReferences');
  const tracingOptions = databaseTracingOptions ?? loadDatabaseTracingOptions(rootPath);

  const tsConfigPath = path.join(rootPath, 'tsconfig.json');
  const tsProject = new Project({
    ...(fs.existsSync(tsConfigPath) ? { tsConfigFilePath: tsConfigPath } : {}),
    skipAddingFilesFromTsConfig: true,
    skipFileDependencyResolution: true,
  });

  const resolutionFiles = loadFullResolutionCatalog
    ? discoverTypeScriptFiles(rootPath)
    : files;
  const projectFiles = [...new Set([...resolutionFiles, ...files].map(normalizeFilePath))];
  if (projectFiles.length > 0) {
    tsProject.addSourceFilesAtPaths(projectFiles);
  }

  const sourceFiles = tsProject.getSourceFiles();
  const emittedFilePaths = new Set(files.map(file => normalizeFilePath(file).toLowerCase()));
  const emittedSourceFiles = sourceFiles.filter(sourceFile =>
    emittedFilePaths.has(normalizeFilePath(sourceFile.getFilePath()).toLowerCase()));

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

  const emittedKnownIds = new Set<string>();
  for (const sourceFile of emittedSourceFiles) {
    collectNodes(sourceFile, rootPath, projectName, nodes, emittedKnownIds, resolveFileRole);
    collectRouteNodes(sourceFile, rootPath, projectName, nodes, emittedKnownIds, resolveFileRole);
    collectConfigurationNodes(sourceFile, rootPath, projectName, nodes, emittedKnownIds, resolveFileRole);
    collectDatabaseTracingNodes(sourceFile, rootPath, projectName, nodes, emittedKnownIds, tracingOptions, resolveFileRole);
  }

  for (const sourceFile of emittedSourceFiles) {
    collectEdges(
      sourceFile,
      rootPath,
      projectName,
      catalogNodes,
      edges,
      catalogKnownIds,
      methodIndex,
      callOutcomes,
      typeReferenceOutcomes,
    );
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
    usedFullResolutionCatalog: loadFullResolutionCatalog,
  };
}

function normalizeFilePath(filePath: string): string {
  return path.resolve(filePath).replace(/\\/g, '/');
}

function indexMethods(nodes: CodeNodeDto[], methodIndex: Map<string, string[]>): void {
  for (const node of nodes) {
    if (node.type !== 'Method') continue;
    const shortName = methodShortName(node.name);
    const ids = methodIndex.get(shortName) ?? [];
    ids.push(node.id);
    methodIndex.set(shortName, ids);
  }
}

function methodShortName(name: string): string {
  const withoutParams = name.split('(')[0];
  const segments = withoutParams.split('.');
  return segments[segments.length - 1] ?? withoutParams;
}
