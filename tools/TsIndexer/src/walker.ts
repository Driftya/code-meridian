import fs from 'node:fs';
import path from 'node:path';
import { Project } from 'ts-morph';
import type { CodeEdgeDto, CodeNodeDto } from './types.js';
import { buildTypeScriptSourceFileGlobs } from './services/project-discovery.js';
import { loadIndexedFileRoleClassifier } from './services/file-roles.js';
import { collectConfigurationEdges, collectConfigurationNodes } from './walker/configuration.js';
import { collectEdges, collectNodes } from './walker/graph.js';
import { collectRouteEdges, collectRouteNodes } from './walker/routes.js';

export interface WalkResult {
  nodes: CodeNodeDto[];
  edges: CodeEdgeDto[];
}

export function walkTypeScript(rootPath: string, projectName: string, files?: string[]): WalkResult {
  const nodes: CodeNodeDto[] = [];
  const edges: CodeEdgeDto[] = [];
  const knownIds = new Set<string>();
  const methodIndex = new Map<string, string[]>();
  const classifyFileRole = loadIndexedFileRoleClassifier(rootPath);

  const tsConfigPath = path.join(rootPath, 'tsconfig.json');
  const tsProject = new Project({
    ...(fs.existsSync(tsConfigPath) ? { tsConfigFilePath: tsConfigPath } : {}),
    skipAddingFilesFromTsConfig: true,
    skipFileDependencyResolution: true,
  });

  if (files) {
    if (files.length > 0) {
      tsProject.addSourceFilesAtPaths(files.map(file => path.resolve(file).replace(/\\/g, '/')));
    }
  } else {
    tsProject.addSourceFilesAtPaths(buildTypeScriptSourceFileGlobs(rootPath));
  }

  const sourceFiles = tsProject.getSourceFiles();

  for (const sourceFile of sourceFiles) {
    collectNodes(sourceFile, rootPath, projectName, nodes, knownIds, classifyFileRole);
  }
  for (const sourceFile of sourceFiles) {
    collectRouteNodes(sourceFile, rootPath, projectName, nodes, knownIds, classifyFileRole);
  }
  for (const sourceFile of sourceFiles) {
    collectConfigurationNodes(sourceFile, rootPath, projectName, nodes, knownIds, classifyFileRole);
  }
  indexMethods(nodes, methodIndex);

  for (const sourceFile of sourceFiles) {
    collectEdges(sourceFile, rootPath, projectName, nodes, edges, knownIds, methodIndex);
    collectRouteEdges(sourceFile, rootPath, projectName, edges, knownIds);
    collectConfigurationEdges(sourceFile, rootPath, projectName, edges);
  }

  return { nodes, edges };
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
