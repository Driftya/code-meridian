import path from 'path';
import fs from 'fs';
import { Project, type SourceFile } from 'ts-morph';
import type { CodeNodeDto, CodeEdgeDto } from './types.js';

export interface WalkResult {
  nodes: CodeNodeDto[];
  edges: CodeEdgeDto[];
}

// ── ID helpers ────────────────────────────────────────────────────────────────

function sanitize(s: string): string {
  return s.replace(/[\\/:*?"<>|]/g, '_');
}

function fileId(project: string, relPath: string): string {
  return `${project}:File:${sanitize(relPath)}`;
}

function nodeId(project: string, relPath: string, name: string, type: string): string {
  return `${project}:${type}:${sanitize(relPath)}:${name}`;
}

// ── Main entry ────────────────────────────────────────────────────────────────

export function walkTypeScript(rootPath: string, projectName: string): WalkResult {
  const nodes: CodeNodeDto[] = [];
  const edges: CodeEdgeDto[] = [];
  const knownIds = new Set<string>();

  const tsConfigPath = path.join(rootPath, 'tsconfig.json');
  const tsProject = new Project({
    ...(fs.existsSync(tsConfigPath) ? { tsConfigFilePath: tsConfigPath } : {}),
    skipAddingFilesFromTsConfig: true,
    skipFileDependencyResolution: true,
  });

  tsProject.addSourceFilesAtPaths([
    path.join(rootPath, '**/*.ts').replace(/\\/g, '/'),
    path.join(rootPath, '**/*.tsx').replace(/\\/g, '/'),
    `!${path.join(rootPath, '**/node_modules/**').replace(/\\/g, '/')}`,
    `!${path.join(rootPath, '**/dist/**').replace(/\\/g, '/')}`,
    `!${path.join(rootPath, '**/build/**').replace(/\\/g, '/')}`,
    `!${path.join(rootPath, '**/*.d.ts').replace(/\\/g, '/')}`,
  ]);

  const sourceFiles = tsProject.getSourceFiles();

  // First pass: collect all nodes so we can validate edge targets
  for (const sourceFile of sourceFiles) {
    collectNodes(sourceFile, rootPath, projectName, nodes, knownIds);
  }

  // Second pass: collect edges (only emit where target is known or is a local file)
  for (const sourceFile of sourceFiles) {
    collectEdges(sourceFile, rootPath, projectName, nodes, edges, knownIds);
  }

  return { nodes, edges };
}

// ── Node collection ───────────────────────────────────────────────────────────

function collectNodes(
  sourceFile: SourceFile,
  rootPath: string,
  projectName: string,
  nodes: CodeNodeDto[],
  knownIds: Set<string>,
): void {
  const relPath = path.relative(rootPath, sourceFile.getFilePath()).replace(/\\/g, '/');
  const fId = fileId(projectName, relPath);

  addNode(nodes, knownIds, {
    id: fId,
    name: path.basename(relPath),
    type: 'File',
    filePath: relPath,
    projectContext: projectName,
  });

  // Classes
  for (const cls of sourceFile.getClasses()) {
    const name = cls.getName() ?? '<anonymous>';
    const id = nodeId(projectName, relPath, name, 'Class');
    addNode(nodes, knownIds, {
      id,
      name,
      type: 'Class',
      filePath: relPath,
      lineNumber: cls.getStartLineNumber(),
      lineCount: cls.getEndLineNumber() - cls.getStartLineNumber() + 1,
      projectContext: projectName,
    });

    for (const method of cls.getMethods()) {
      const mName = `${name}.${method.getName()}`;
      const mId = nodeId(projectName, relPath, mName, 'Method');
      addNode(nodes, knownIds, {
        id: mId,
        name: mName,
        type: 'Method',
        filePath: relPath,
        lineNumber: method.getStartLineNumber(),
        lineCount: method.getEndLineNumber() - method.getStartLineNumber() + 1,
        projectContext: projectName,
      });
    }

    for (const prop of cls.getProperties()) {
      const pName = `${name}.${prop.getName()}`;
      const pId = nodeId(projectName, relPath, pName, 'Property');
      addNode(nodes, knownIds, {
        id: pId,
        name: pName,
        type: 'Property',
        filePath: relPath,
        lineNumber: prop.getStartLineNumber(),
        projectContext: projectName,
      });
    }
  }

  // Interfaces
  for (const iface of sourceFile.getInterfaces()) {
    const name = iface.getName();
    const id = nodeId(projectName, relPath, name, 'Interface');
    addNode(nodes, knownIds, {
      id,
      name,
      type: 'Interface',
      filePath: relPath,
      lineNumber: iface.getStartLineNumber(),
      lineCount: iface.getEndLineNumber() - iface.getStartLineNumber() + 1,
      projectContext: projectName,
    });

    for (const method of iface.getMethods()) {
      const mName = `${name}.${method.getName()}`;
      const mId = nodeId(projectName, relPath, mName, 'Method');
      addNode(nodes, knownIds, {
        id: mId,
        name: mName,
        type: 'Method',
        filePath: relPath,
        lineNumber: method.getStartLineNumber(),
        lineCount: method.getEndLineNumber() - method.getStartLineNumber() + 1,
        projectContext: projectName,
      });
    }
  }

  // Top-level functions
  for (const fn of sourceFile.getFunctions()) {
    const name = fn.getName() ?? '<anonymous>';
    const id = nodeId(projectName, relPath, name, 'Method');
    addNode(nodes, knownIds, {
      id,
      name,
      type: 'Method',
      filePath: relPath,
      lineNumber: fn.getStartLineNumber(),
      lineCount: fn.getEndLineNumber() - fn.getStartLineNumber() + 1,
      projectContext: projectName,
    });
  }

  // Enums
  for (const enumDecl of sourceFile.getEnums()) {
    const name = enumDecl.getName();
    const id = nodeId(projectName, relPath, name, 'Enum');
    addNode(nodes, knownIds, {
      id,
      name,
      type: 'Enum',
      filePath: relPath,
      lineNumber: enumDecl.getStartLineNumber(),
      projectContext: projectName,
    });
  }
}

// ── Edge collection ───────────────────────────────────────────────────────────

function collectEdges(
  sourceFile: SourceFile,
  rootPath: string,
  projectName: string,
  nodes: CodeNodeDto[],
  edges: CodeEdgeDto[],
  knownIds: Set<string>,
): void {
  const relPath = path.relative(rootPath, sourceFile.getFilePath()).replace(/\\/g, '/');
  const fId = fileId(projectName, relPath);

  // File → Class/Interface/Enum/Function (Contains)
  for (const cls of sourceFile.getClasses()) {
    const name = cls.getName() ?? '<anonymous>';
    const cId = nodeId(projectName, relPath, name, 'Class');
    if (knownIds.has(cId)) edges.push({ sourceId: fId, targetId: cId, type: 'Contains' });

    for (const method of cls.getMethods()) {
      const mId = nodeId(projectName, relPath, `${name}.${method.getName()}`, 'Method');
      if (knownIds.has(mId)) edges.push({ sourceId: cId, targetId: mId, type: 'Contains' });
    }

    for (const prop of cls.getProperties()) {
      const pId = nodeId(projectName, relPath, `${name}.${prop.getName()}`, 'Property');
      if (knownIds.has(pId)) edges.push({ sourceId: cId, targetId: pId, type: 'Contains' });
    }

    // Inherits — only within indexed project
    const baseClass = cls.getBaseClass();
    if (baseClass) {
      const baseName = baseClass.getName();
      if (baseName) {
        const baseSourceFile = baseClass.getSourceFile();
        const baseRelPath = path.relative(rootPath, baseSourceFile.getFilePath()).replace(/\\/g, '/');
        const baseId = nodeId(projectName, baseRelPath, baseName, 'Class');
        if (knownIds.has(baseId)) edges.push({ sourceId: cId, targetId: baseId, type: 'Inherits' });
      }
    }

    // Implements — only within indexed project
    for (const impl of cls.getImplements()) {
      const ifaceName = impl.getExpression().getText().split('<')[0]; // strip generics
      // Try to find the interface in any indexed file
      const matchingId = findInterfaceId(ifaceName, projectName, knownIds);
      if (matchingId) edges.push({ sourceId: cId, targetId: matchingId, type: 'Implements' });
    }
  }

  for (const iface of sourceFile.getInterfaces()) {
    const name = iface.getName();
    const iId = nodeId(projectName, relPath, name, 'Interface');
    if (knownIds.has(iId)) edges.push({ sourceId: fId, targetId: iId, type: 'Contains' });

    for (const method of iface.getMethods()) {
      const mId = nodeId(projectName, relPath, `${name}.${method.getName()}`, 'Method');
      if (knownIds.has(mId)) edges.push({ sourceId: iId, targetId: mId, type: 'Contains' });
    }

    // Interface extends interface
    for (const ext of iface.getExtends()) {
      const extName = ext.getExpression().getText().split('<')[0];
      const matchingId = findInterfaceId(extName, projectName, knownIds);
      if (matchingId) edges.push({ sourceId: iId, targetId: matchingId, type: 'Inherits' });
    }
  }

  for (const fn of sourceFile.getFunctions()) {
    const name = fn.getName() ?? '<anonymous>';
    const fnId = nodeId(projectName, relPath, name, 'Method');
    if (knownIds.has(fnId)) edges.push({ sourceId: fId, targetId: fnId, type: 'Contains' });
  }

  for (const enumDecl of sourceFile.getEnums()) {
    const eId = nodeId(projectName, relPath, enumDecl.getName(), 'Enum');
    if (knownIds.has(eId)) edges.push({ sourceId: fId, targetId: eId, type: 'Contains' });
  }

  // Local import dependencies (File DependsOn File)
  for (const importDecl of sourceFile.getImportDeclarations()) {
    if (!importDecl.getModuleSpecifierValue().startsWith('.')) continue;
    const resolved = importDecl.getModuleSpecifierSourceFile();
    if (!resolved) continue;
    const targetRelPath = path.relative(rootPath, resolved.getFilePath()).replace(/\\/g, '/');
    const targetId = fileId(projectName, targetRelPath);
    if (knownIds.has(targetId)) edges.push({ sourceId: fId, targetId: targetId, type: 'DependsOn' });
  }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function addNode(nodes: CodeNodeDto[], knownIds: Set<string>, node: CodeNodeDto): void {
  if (!knownIds.has(node.id)) {
    knownIds.add(node.id);
    nodes.push(node);
  }
}

/** Scan knownIds for an Interface node matching the given short name. */
function findInterfaceId(shortName: string, projectName: string, knownIds: Set<string>): string | undefined {
  const suffix = `:Interface:`;
  for (const id of knownIds) {
    if (id.startsWith(`${projectName}${suffix}`) && id.endsWith(`:${shortName}`)) {
      return id;
    }
  }
  return undefined;
}
