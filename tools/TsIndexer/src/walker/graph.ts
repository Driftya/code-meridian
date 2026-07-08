import path from 'node:path';
import type { CallExpression, Node, PropertyAccessExpression, SourceFile, Symbol as TsSymbol, TypeReferenceNode, VariableDeclaration } from 'ts-morph';
import { SyntaxKind } from 'ts-morph';
import type { CodeEdgeDto, CodeNodeDto } from '../types.js';
import { addNode, fileId, getNamespaceForPath, hashText, isTestFilePath, nodeId, sourceHash, sourceSnippet } from './common.js';
import { extractIndexedTestCases } from './test-discovery.js';

export function collectNodes(
  sourceFile: SourceFile,
  rootPath: string,
  projectName: string,
  nodes: CodeNodeDto[],
  knownIds: Set<string>,
  classifyFileRole?: (relativePath: string) => string | undefined,
): void {
  const relPath = path.relative(rootPath, sourceFile.getFilePath()).replace(/\\/g, '/');
  const namespace = getNamespaceForPath(relPath, isTestFilePath(relPath));
  const fId = fileId(projectName, relPath);

  if (namespace) {
    const moduleId = `${projectName}:Module:${sanitizeNamespace(namespace)}`;
    addNode(nodes, knownIds, {
      id: moduleId,
      name: namespace,
      type: 'Module',
      namespace,
      filePath: relPath,
      lineNumber: 1,
      lineCount: sourceFile.getEndLineNumber(),
      projectContext: projectName,
    }, classifyFileRole);
  }

  addNode(nodes, knownIds, {
    id: fId,
    name: path.basename(relPath),
    type: 'File',
    namespace,
    filePath: relPath,
    lineNumber: 1,
    lineCount: sourceFile.getEndLineNumber(),
    sourceHash: hashText(sourceFile.getFullText()),
    projectContext: projectName,
  }, classifyFileRole);

  for (const cls of sourceFile.getClasses()) {
    const name = cls.getName() ?? '<anonymous>';
    const id = nodeId(projectName, relPath, name, 'Class');
    addNode(nodes, knownIds, {
      id,
      name,
      type: 'Class',
      namespace,
      filePath: relPath,
      lineNumber: cls.getStartLineNumber(),
      lineCount: cls.getEndLineNumber() - cls.getStartLineNumber() + 1,
      sourceSnippet: sourceSnippet(sourceFile, cls.getStartLineNumber(), cls.getEndLineNumber()),
      sourceHash: sourceHash(sourceFile, cls.getStartLineNumber(), cls.getEndLineNumber()),
      projectContext: projectName,
    }, classifyFileRole);

    for (const method of cls.getMethods()) {
      const mName = `${name}.${method.getName()}`;
      const mId = nodeId(projectName, relPath, mName, 'Method');
      addNode(nodes, knownIds, {
        id: mId,
        name: mName,
        type: 'Method',
        namespace,
        filePath: relPath,
        lineNumber: method.getStartLineNumber(),
        lineCount: method.getEndLineNumber() - method.getStartLineNumber() + 1,
        sourceSnippet: sourceSnippet(sourceFile, method.getStartLineNumber(), method.getEndLineNumber()),
        sourceHash: sourceHash(sourceFile, method.getStartLineNumber(), method.getEndLineNumber()),
        projectContext: projectName,
      }, classifyFileRole);
    }

    for (const ctor of cls.getConstructors()) {
      const ctorName = `${name}.constructor`;
      const ctorId = nodeId(projectName, relPath, ctorName, 'Method');
      addNode(nodes, knownIds, {
        id: ctorId,
        name: ctorName,
        type: 'Method',
        namespace,
        filePath: relPath,
        lineNumber: ctor.getStartLineNumber(),
        lineCount: ctor.getEndLineNumber() - ctor.getStartLineNumber() + 1,
        sourceSnippet: sourceSnippet(sourceFile, ctor.getStartLineNumber(), ctor.getEndLineNumber()),
        sourceHash: sourceHash(sourceFile, ctor.getStartLineNumber(), ctor.getEndLineNumber()),
        projectContext: projectName,
      }, classifyFileRole);
    }

    for (const prop of cls.getProperties()) {
      const pName = `${name}.${prop.getName()}`;
      const pId = nodeId(projectName, relPath, pName, 'Property');
      addNode(nodes, knownIds, {
        id: pId,
        name: pName,
        type: 'Property',
        namespace,
        filePath: relPath,
        lineNumber: prop.getStartLineNumber(),
        lineCount: prop.getEndLineNumber() - prop.getStartLineNumber() + 1,
        sourceSnippet: sourceSnippet(sourceFile, prop.getStartLineNumber(), prop.getEndLineNumber()),
        sourceHash: sourceHash(sourceFile, prop.getStartLineNumber(), prop.getEndLineNumber()),
        projectContext: projectName,
      }, classifyFileRole);
    }
  }

  for (const iface of sourceFile.getInterfaces()) {
    const name = iface.getName();
    const id = nodeId(projectName, relPath, name, 'Interface');
    addNode(nodes, knownIds, {
      id,
      name,
      type: 'Interface',
      namespace,
      filePath: relPath,
      lineNumber: iface.getStartLineNumber(),
      lineCount: iface.getEndLineNumber() - iface.getStartLineNumber() + 1,
      sourceSnippet: sourceSnippet(sourceFile, iface.getStartLineNumber(), iface.getEndLineNumber()),
      sourceHash: sourceHash(sourceFile, iface.getStartLineNumber(), iface.getEndLineNumber()),
      projectContext: projectName,
    }, classifyFileRole);

    for (const method of iface.getMethods()) {
      const mName = `${name}.${method.getName()}`;
      const mId = nodeId(projectName, relPath, mName, 'Method');
      addNode(nodes, knownIds, {
        id: mId,
        name: mName,
        type: 'Method',
        namespace,
        filePath: relPath,
        lineNumber: method.getStartLineNumber(),
        lineCount: method.getEndLineNumber() - method.getStartLineNumber() + 1,
        sourceSnippet: sourceSnippet(sourceFile, method.getStartLineNumber(), method.getEndLineNumber()),
        sourceHash: sourceHash(sourceFile, method.getStartLineNumber(), method.getEndLineNumber()),
        projectContext: projectName,
      }, classifyFileRole);
    }
  }

  for (const fn of sourceFile.getFunctions()) {
    const name = fn.getName() ?? '<anonymous>';
    const id = nodeId(projectName, relPath, name, 'Method');
    addNode(nodes, knownIds, {
      id,
      name,
      type: 'Method',
      namespace,
      filePath: relPath,
      lineNumber: fn.getStartLineNumber(),
      lineCount: fn.getEndLineNumber() - fn.getStartLineNumber() + 1,
      sourceSnippet: sourceSnippet(sourceFile, fn.getStartLineNumber(), fn.getEndLineNumber()),
      sourceHash: sourceHash(sourceFile, fn.getStartLineNumber(), fn.getEndLineNumber()),
      projectContext: projectName,
    }, classifyFileRole);
  }

  for (const variable of getTopLevelFunctionVariables(sourceFile)) {
    const name = variable.getName();
    const id = nodeId(projectName, relPath, name, 'Method');
    addNode(nodes, knownIds, {
      id,
      name,
      type: 'Method',
      namespace,
      filePath: relPath,
      lineNumber: variable.getStartLineNumber(),
      lineCount: variable.getEndLineNumber() - variable.getStartLineNumber() + 1,
      sourceSnippet: sourceSnippet(sourceFile, variable.getStartLineNumber(), variable.getEndLineNumber()),
      sourceHash: sourceHash(sourceFile, variable.getStartLineNumber(), variable.getEndLineNumber()),
      projectContext: projectName,
    }, classifyFileRole);
  }

  for (const typeAlias of sourceFile.getTypeAliases()) {
    const name = typeAlias.getName();
    const id = nodeId(projectName, relPath, name, 'Interface');
    addNode(nodes, knownIds, {
      id,
      name,
      type: 'Interface',
      namespace,
      filePath: relPath,
      lineNumber: typeAlias.getStartLineNumber(),
      lineCount: typeAlias.getEndLineNumber() - typeAlias.getStartLineNumber() + 1,
      sourceSnippet: sourceSnippet(sourceFile, typeAlias.getStartLineNumber(), typeAlias.getEndLineNumber()),
      sourceHash: sourceHash(sourceFile, typeAlias.getStartLineNumber(), typeAlias.getEndLineNumber()),
      projectContext: projectName,
    }, classifyFileRole);
  }

  for (const testCase of extractIndexedTestCases(sourceFile, projectName, relPath)) {
    addNode(nodes, knownIds, {
      id: testCase.id,
      name: testCase.name,
      type: 'Method',
      namespace,
      filePath: relPath,
      lineNumber: testCase.lineNumber,
      lineCount: testCase.lineCount,
      sourceSnippet: sourceSnippet(sourceFile, testCase.lineNumber, testCase.lineNumber + testCase.lineCount - 1),
      sourceHash: sourceHash(sourceFile, testCase.lineNumber, testCase.lineNumber + testCase.lineCount - 1),
      projectContext: projectName,
    }, classifyFileRole);
  }

  for (const enumDecl of sourceFile.getEnums()) {
    const name = enumDecl.getName();
    const id = nodeId(projectName, relPath, name, 'Enum');
    addNode(nodes, knownIds, {
      id,
      name,
      type: 'Enum',
      namespace,
      filePath: relPath,
      lineNumber: enumDecl.getStartLineNumber(),
      lineCount: enumDecl.getEndLineNumber() - enumDecl.getStartLineNumber() + 1,
      sourceSnippet: sourceSnippet(sourceFile, enumDecl.getStartLineNumber(), enumDecl.getEndLineNumber()),
      sourceHash: sourceHash(sourceFile, enumDecl.getStartLineNumber(), enumDecl.getEndLineNumber()),
      projectContext: projectName,
    }, classifyFileRole);
  }
}

export function collectEdges(
  sourceFile: SourceFile,
  rootPath: string,
  projectName: string,
  _nodes: CodeNodeDto[],
  edges: CodeEdgeDto[],
  knownIds: Set<string>,
  methodIndex: Map<string, string[]>,
): void {
  const relPath = path.relative(rootPath, sourceFile.getFilePath()).replace(/\\/g, '/');
  const fId = fileId(projectName, relPath);

  for (const cls of sourceFile.getClasses()) {
    const name = cls.getName() ?? '<anonymous>';
    const cId = nodeId(projectName, relPath, name, 'Class');
    if (knownIds.has(cId)) edges.push({ sourceId: fId, targetId: cId, type: 'Contains' });

    for (const method of cls.getMethods()) {
      const mId = nodeId(projectName, relPath, `${name}.${method.getName()}`, 'Method');
      if (knownIds.has(mId)) edges.push({ sourceId: cId, targetId: mId, type: 'Contains' });
      addCallEdges(projectName, rootPath, knownIds, mId, method.getDescendantsOfKind(SyntaxKind.CallExpression), edges, methodIndex, {
        filePath: relPath,
        className: name,
      });
      addTypeUseEdges(projectName, rootPath, relPath, method, mId, edges, knownIds);
    }

    for (const ctor of cls.getConstructors()) {
      const ctorId = nodeId(projectName, relPath, `${name}.constructor`, 'Method');
      if (knownIds.has(ctorId)) edges.push({ sourceId: cId, targetId: ctorId, type: 'Contains' });
      addCallEdges(projectName, rootPath, knownIds, ctorId, ctor.getDescendantsOfKind(SyntaxKind.CallExpression), edges, methodIndex, {
        filePath: relPath,
        className: name,
      });
      addTypeUseEdges(projectName, rootPath, relPath, ctor, ctorId, edges, knownIds);
    }

    for (const prop of cls.getProperties()) {
      const pId = nodeId(projectName, relPath, `${name}.${prop.getName()}`, 'Property');
      if (knownIds.has(pId)) edges.push({ sourceId: cId, targetId: pId, type: 'Contains' });
      addTypeUseEdges(projectName, rootPath, relPath, prop, pId, edges, knownIds);
    }

    const baseClass = cls.getBaseClass();
    if (baseClass) {
      const baseId = resolveHeritageTargetId(projectName, rootPath, baseClass, knownIds);
      if (baseId) edges.push({ sourceId: cId, targetId: baseId, type: 'Inherits' });
    }

    for (const impl of cls.getImplements()) {
      const ifaceName = impl.getExpression().getText().split('<')[0];
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
      addTypeUseEdges(projectName, rootPath, relPath, method, mId, edges, knownIds);
    }

    for (const ext of iface.getExtends()) {
      const matchingId = resolveHeritageTargetId(projectName, rootPath, ext, knownIds);
      if (matchingId) edges.push({ sourceId: iId, targetId: matchingId, type: 'Inherits' });
    }
  }

  for (const fn of sourceFile.getFunctions()) {
    const name = fn.getName() ?? '<anonymous>';
    const fnId = nodeId(projectName, relPath, name, 'Method');
    if (knownIds.has(fnId)) edges.push({ sourceId: fId, targetId: fnId, type: 'Contains' });
    addCallEdges(projectName, rootPath, knownIds, fnId, fn.getDescendantsOfKind(SyntaxKind.CallExpression), edges, methodIndex, {
      filePath: relPath,
    });
    addTypeUseEdges(projectName, rootPath, relPath, fn, fnId, edges, knownIds);
  }

  for (const variable of getTopLevelFunctionVariables(sourceFile)) {
    const variableId = nodeId(projectName, relPath, variable.getName(), 'Method');
    if (knownIds.has(variableId)) edges.push({ sourceId: fId, targetId: variableId, type: 'Contains' });
    const initializer = variable.getInitializerIfKind(SyntaxKind.ArrowFunction)
      ?? variable.getInitializerIfKind(SyntaxKind.FunctionExpression);
    if (!initializer) continue;

    addCallEdges(projectName, rootPath, knownIds, variableId, initializer.getDescendantsOfKind(SyntaxKind.CallExpression), edges, methodIndex, {
      filePath: relPath,
    });
    addTypeUseEdges(projectName, rootPath, relPath, variable, variableId, edges, knownIds);
  }

  for (const typeAlias of sourceFile.getTypeAliases()) {
    const aliasId = nodeId(projectName, relPath, typeAlias.getName(), 'Interface');
    if (knownIds.has(aliasId)) edges.push({ sourceId: fId, targetId: aliasId, type: 'Contains' });
    addTypeUseEdges(projectName, rootPath, relPath, typeAlias, aliasId, edges, knownIds);
  }

  for (const testCase of extractIndexedTestCases(sourceFile, projectName, relPath)) {
    if (knownIds.has(testCase.id)) {
      edges.push({ sourceId: fId, targetId: testCase.id, type: 'Contains' });
    }

    addCallEdges(
      projectName,
      rootPath,
      knownIds,
      testCase.id,
      testCase.callback.getDescendantsOfKind(SyntaxKind.CallExpression),
      edges,
      methodIndex,
      { filePath: relPath },
    );
  }

  for (const enumDecl of sourceFile.getEnums()) {
    const eId = nodeId(projectName, relPath, enumDecl.getName(), 'Enum');
    if (knownIds.has(eId)) edges.push({ sourceId: fId, targetId: eId, type: 'Contains' });
  }

  for (const importDecl of sourceFile.getImportDeclarations()) {
    if (!importDecl.getModuleSpecifierValue().startsWith('.')) continue;
    const resolved = importDecl.getModuleSpecifierSourceFile();
    if (!resolved) continue;
    const targetRelPath = path.relative(rootPath, resolved.getFilePath()).replace(/\\/g, '/');
    const targetId = fileId(projectName, targetRelPath);
    if (knownIds.has(targetId)) edges.push({ sourceId: fId, targetId: targetId, type: 'DependsOn' });

    const importClause = importDecl.getImportClause();
    const defaultImport = importClause?.getDefaultImport();
    if (defaultImport) {
      const importedTargetId = resolveDefaultExportedTypeNodeId(projectName, rootPath, resolved, knownIds);
      if (importedTargetId) {
        edges.push({ sourceId: fId, targetId: importedTargetId, type: 'Uses' });
      }
    }

    const namedBindings = importClause?.getNamedBindings();
    if (namedBindings?.isKind(SyntaxKind.NamedImports)) {
      for (const specifier of namedBindings.getElements()) {
        const importedName = specifier.getNameNode().getText();
        const importedTargetId = resolveExportedTypeNodeId(projectName, rootPath, resolved, importedName, knownIds);
        if (importedTargetId) {
          edges.push({ sourceId: fId, targetId: importedTargetId, type: 'Uses' });
        }
      }
    }
  }
}

function addCallEdges(
  projectName: string,
  rootPath: string,
  knownIds: Set<string>,
  sourceId: string,
  calls: CallExpression[],
  edges: CodeEdgeDto[],
  methodIndex: Map<string, string[]>,
  source: { filePath: string; className?: string },
): void {
  for (const call of calls) {
    const symbolTargetId = resolveCallTargetId(projectName, rootPath, call, knownIds);
    if (symbolTargetId && symbolTargetId !== sourceId) {
      edges.push({ sourceId, targetId: symbolTargetId, type: 'Calls' });
      continue;
    }

    const calleeName = calleeShortName(call);
    if (!calleeName) continue;

    const candidates = methodIndex.get(calleeName) ?? [];
    const targetId = selectCallTarget(candidates, source, calleeName);
    if (!targetId || targetId === sourceId) continue;

    edges.push({ sourceId, targetId, type: 'Calls' });
  }
}

function resolveCallTargetId(
  projectName: string,
  rootPath: string,
  call: CallExpression,
  knownIds: Set<string>,
): string | undefined {
  const expression = call.getExpression();
  const symbol = expression.getSymbol();
  const fallbackName = calleeShortName(call);
  if (!fallbackName) return undefined;
  const direct = resolveSymbolTargetId(projectName, rootPath, symbol, fallbackName, knownIds);
  if (direct) return direct;

  const memberTarget = resolvePropertyAccessCallTargetId(projectName, rootPath, expression, fallbackName, knownIds);
  if (memberTarget) return memberTarget;

  if (expression.getKind() === SyntaxKind.Identifier) {
    const identifier = expression.asKindOrThrow(SyntaxKind.Identifier);
    for (const definition of identifier.getDefinitionNodes()) {
      if (definition.getKind() === SyntaxKind.ImportSpecifier) {
        const importSpecifier = definition.asKindOrThrow(SyntaxKind.ImportSpecifier);
        const resolved = importSpecifier.getImportDeclaration().getModuleSpecifierSourceFile();
        if (!resolved) continue;

        const importedName = importSpecifier.getNameNode().getText();
        const importedTarget = resolveExportedMethodNodeId(projectName, rootPath, resolved, importedName, knownIds);
        if (importedTarget) return importedTarget;
      }
    }
  }

  return undefined;
}

function resolvePropertyAccessCallTargetId(
  projectName: string,
  rootPath: string,
  expression: Node,
  memberName: string,
  knownIds: Set<string>,
): string | undefined {
  if (expression.getKind() !== SyntaxKind.PropertyAccessExpression) {
    return undefined;
  }

  const propertyAccess = expression.asKindOrThrow(SyntaxKind.PropertyAccessExpression);
  const owner = propertyAccess.getExpression();
  const candidates = new Set<string>();

  for (const declaration of owner.getType().getSymbol()?.getDeclarations() ?? []) {
    const targetId = resolveTypeMemberTargetId(projectName, rootPath, declaration, memberName, knownIds);
    if (targetId) {
      candidates.add(targetId);
    }
  }

  if (candidates.size === 1) {
    return [...candidates][0];
  }

  return undefined;
}

function resolveTypeMemberTargetId(
  projectName: string,
  rootPath: string,
  declaration: Node,
  memberName: string,
  knownIds: Set<string>,
): string | undefined {
  const sourceFile = declaration.getSourceFile();
  const relPath = path.relative(rootPath, sourceFile.getFilePath()).replace(/\\/g, '/');

  if (declaration.getKind() === SyntaxKind.ClassDeclaration) {
    const className = declaration.asKindOrThrow(SyntaxKind.ClassDeclaration).getName();
    if (!className) return undefined;
    const targetId = nodeId(projectName, relPath, `${className}.${memberName}`, 'Method');
    return knownIds.has(targetId) ? targetId : undefined;
  }

  if (declaration.getKind() === SyntaxKind.InterfaceDeclaration) {
    const interfaceName = declaration.asKindOrThrow(SyntaxKind.InterfaceDeclaration).getName();
    const targetId = nodeId(projectName, relPath, `${interfaceName}.${memberName}`, 'Method');
    return knownIds.has(targetId) ? targetId : undefined;
  }

  if (declaration.getKind() === SyntaxKind.TypeAliasDeclaration) {
    const aliasName = declaration.asKindOrThrow(SyntaxKind.TypeAliasDeclaration).getName();
    const targetId = nodeId(projectName, relPath, `${aliasName}.${memberName}`, 'Method');
    return knownIds.has(targetId) ? targetId : undefined;
  }

  return undefined;
}

function selectCallTarget(
  candidates: string[],
  source: { filePath: string; className?: string },
  calleeName: string,
): string | undefined {
  if (candidates.length === 0) return undefined;
  if (candidates.length === 1) return candidates[0];

  if (source.className) {
    const sameClass = candidates.filter(id => id.endsWith(`:${source.className}.${calleeName}`));
    if (sameClass.length === 1) return sameClass[0];
  }

  const sameFileToken = sanitizeNamespace(source.filePath);
  const sameFile = candidates.filter(id => id.includes(`:${sameFileToken}:`));
  return sameFile.length === 1 ? sameFile[0] : undefined;
}

function calleeShortName(call: CallExpression): string | undefined {
  const expression = call.getExpression().getText();
  const match = /([A-Za-z_$][\w$]*)\s*$/.exec(expression.split('<')[0]);
  return match?.[1];
}

function findInterfaceId(shortName: string, projectName: string, knownIds: Set<string>): string | undefined {
  const suffix = `:Interface:`;
  for (const id of knownIds) {
    if (id.startsWith(`${projectName}${suffix}`) && id.endsWith(`:${shortName}`)) {
      return id;
    }
  }
  return undefined;
}

function addTypeUseEdges(
  projectName: string,
  rootPath: string,
  relPath: string,
  node: Node,
  sourceId: string,
  edges: CodeEdgeDto[],
  knownIds: Set<string>,
): void {
  const typeReferences = node.getDescendantsOfKind(SyntaxKind.TypeReference);
  for (const typeRef of typeReferences) {
    const targetId = resolveTypeReference(projectName, rootPath, relPath, typeRef, knownIds);
    if (targetId && targetId !== sourceId) {
      edges.push({ sourceId, targetId, type: 'Uses' });
    }
  }
}

function resolveTypeReference(
  projectName: string,
  rootPath: string,
  _relPath: string,
  typeRef: TypeReferenceNode,
  knownIds: Set<string>,
): string | undefined {
  const symbol = typeRef.getTypeName().getSymbol();
  return resolveSymbolTargetId(projectName, rootPath, symbol, typeRef.getText(), knownIds);
}

function resolveExportedTypeNodeId(
  projectName: string,
  rootPath: string,
  sourceFile: SourceFile,
  exportedName: string,
  knownIds: Set<string>,
): string | undefined {
  const relPath = path.relative(rootPath, sourceFile.getFilePath()).replace(/\\/g, '/');
  for (const type of ['Class', 'Interface', 'Enum'] as const) {
    const targetId = nodeId(projectName, relPath, exportedName, type);
    if (knownIds.has(targetId)) return targetId;
  }

  for (const exportDecl of sourceFile.getExportDeclarations()) {
    const namedExports = exportDecl.getNamedExports();
    for (const namedExport of namedExports) {
      const alias = namedExport.getAliasNode()?.getText();
      const name = namedExport.getNameNode().getText();
      if (alias !== exportedName && name !== exportedName) continue;

      const resolved = exportDecl.getModuleSpecifierSourceFile();
      if (!resolved) continue;

      const resolvedId = resolveExportedTypeNodeId(projectName, rootPath, resolved, name, knownIds);
      if (resolvedId) return resolvedId;
    }
  }

  return undefined;
}

function resolveDefaultExportedTypeNodeId(
  projectName: string,
  rootPath: string,
  sourceFile: SourceFile,
  knownIds: Set<string>,
): string | undefined {
  const relPath = path.relative(rootPath, sourceFile.getFilePath()).replace(/\\/g, '/');
  for (const cls of sourceFile.getClasses()) {
    if (cls.isDefaultExport()) {
      const name = cls.getName();
      if (!name) continue;
      const targetId = nodeId(projectName, relPath, name, 'Class');
      if (knownIds.has(targetId)) return targetId;
    }
  }

  for (const iface of sourceFile.getInterfaces()) {
    if (iface.isDefaultExport()) {
      const targetId = nodeId(projectName, relPath, iface.getName(), 'Interface');
      if (knownIds.has(targetId)) return targetId;
    }
  }

  for (const typeAlias of sourceFile.getTypeAliases()) {
    if (typeAlias.isDefaultExport()) {
      const targetId = nodeId(projectName, relPath, typeAlias.getName(), 'Interface');
      if (knownIds.has(targetId)) return targetId;
    }
  }

  for (const enumDecl of sourceFile.getEnums()) {
    if (enumDecl.isDefaultExport()) {
      const targetId = nodeId(projectName, relPath, enumDecl.getName(), 'Enum');
      if (knownIds.has(targetId)) return targetId;
    }
  }

  const defaultExportSymbol = sourceFile.getDefaultExportSymbol();
  return resolveSymbolTargetId(projectName, rootPath, defaultExportSymbol, 'default', knownIds);
}

function resolveExportedMethodNodeId(
  projectName: string,
  rootPath: string,
  sourceFile: SourceFile,
  exportedName: string,
  knownIds: Set<string>,
): string | undefined {
  const relPath = path.relative(rootPath, sourceFile.getFilePath()).replace(/\\/g, '/');
  const directTargetId = nodeId(projectName, relPath, exportedName, 'Method');
  if (knownIds.has(directTargetId)) return directTargetId;

  for (const exportDecl of sourceFile.getExportDeclarations()) {
    const namedExports = exportDecl.getNamedExports();
    for (const namedExport of namedExports) {
      const alias = namedExport.getAliasNode()?.getText();
      const name = namedExport.getNameNode().getText();
      if (alias !== exportedName && name !== exportedName) continue;

      const resolved = exportDecl.getModuleSpecifierSourceFile();
      if (!resolved) continue;

      const resolvedId = resolveExportedMethodNodeId(projectName, rootPath, resolved, name, knownIds);
      if (resolvedId) return resolvedId;
    }
  }

  return undefined;
}

function resolveSymbolTargetId(
  projectName: string,
  rootPath: string,
  symbol: TsSymbol | undefined,
  fallbackName: string,
  knownIds: Set<string>,
): string | undefined {
  const visited = new Set<string>();
  return resolveSymbolTargetIdCore(projectName, rootPath, symbol, fallbackName, knownIds, visited);
}

function resolveSymbolTargetIdCore(
  projectName: string,
  rootPath: string,
  symbol: TsSymbol | undefined,
  fallbackName: string,
  knownIds: Set<string>,
  visited: Set<string>,
): string | undefined {
  if (!symbol) return undefined;

  const key = symbol.getFullyQualifiedName();
  if (visited.has(key)) return undefined;
  visited.add(key);

  const aliased = symbol.getAliasedSymbol?.();
  if (aliased && aliased !== symbol) {
    const resolved = resolveSymbolTargetIdCore(projectName, rootPath, aliased, fallbackName, knownIds, visited);
    if (resolved) return resolved;
  }

  for (const declaration of symbol.getDeclarations()) {
    const declarationId = resolveDeclarationTargetId(projectName, rootPath, declaration, knownIds);
    if (declarationId) return declarationId;
  }

  const sourceDeclarations = symbol.getDeclarations();
  if (sourceDeclarations.length > 0) {
    const first = sourceDeclarations[0];
    const relPath = path.relative(rootPath, first.getSourceFile().getFilePath()).replace(/\\/g, '/');
    for (const type of ['Class', 'Interface', 'Enum'] as const) {
      const targetId = nodeId(projectName, relPath, fallbackName, type);
      if (knownIds.has(targetId)) return targetId;
    }
  }

  return undefined;
}

function resolveDeclarationTargetId(
  projectName: string,
  rootPath: string,
  declaration: Node,
  knownIds: Set<string>,
): string | undefined {
  const sourceFile = declaration.getSourceFile();
  const relPath = path.relative(rootPath, sourceFile.getFilePath()).replace(/\\/g, '/');
  const symbolName = declaration.getSymbol()?.getName();
  const name = declaration.getKindName() === 'MethodDeclaration'
    ? buildMethodDeclarationName(declaration)
    : symbolName ?? declaration.getText().split(/\s+/)[0];

  const kind = declaration.getKindName();
  const targetType = kind === 'InterfaceDeclaration'
    ? 'Interface'
    : kind === 'ClassDeclaration'
      ? 'Class'
      : kind === 'EnumDeclaration'
        ? 'Enum'
        : kind === 'TypeAliasDeclaration'
          ? 'Interface'
          : kind === 'FunctionDeclaration' || kind === 'MethodDeclaration' || kind === 'MethodSignature' || kind === 'Constructor' || kind === 'VariableDeclaration'
            ? 'Method'
            : undefined;

  if (!targetType) return undefined;

  const targetId = nodeId(projectName, relPath, name, targetType);
  return knownIds.has(targetId) ? targetId : undefined;
}

function buildMethodDeclarationName(declaration: Node): string {
  if (declaration.getKindName() === 'Constructor') {
    const ctor = declaration.asKindOrThrow(SyntaxKind.Constructor);
    const className = ctor.getFirstAncestorByKind(SyntaxKind.ClassDeclaration)?.getName();
    return className ? `${className}.constructor` : 'constructor';
  }

  if (declaration.getKindName() === 'VariableDeclaration') {
    const variable = declaration.asKindOrThrow(SyntaxKind.VariableDeclaration);
    return variable.getName();
  }

  if (declaration.getKindName() !== 'MethodDeclaration' && declaration.getKindName() !== 'MethodSignature') {
    return declaration.getSymbol()?.getName() ?? declaration.getText().split(/\s+/)[0];
  }

  const methodName = 'getName' in declaration && typeof declaration.getName === 'function'
    ? declaration.getName()
    : declaration.getSymbol()?.getName() ?? declaration.getText().split(/\s+/)[0];
  const classDecl = declaration.getFirstAncestorByKind(SyntaxKind.ClassDeclaration);
  if (classDecl?.getName()) {
    return `${classDecl.getName()}.${methodName}`;
  }

  const interfaceDecl = declaration.getFirstAncestorByKind(SyntaxKind.InterfaceDeclaration);
  if (interfaceDecl?.getName()) {
    return `${interfaceDecl.getName()}.${methodName}`;
  }

  return methodName;
}

function getTopLevelFunctionVariables(sourceFile: SourceFile): VariableDeclaration[] {
  return sourceFile
    .getVariableStatements()
    .flatMap(statement => statement.getDeclarations())
    .filter(isFunctionValuedVariable);
}

function isFunctionValuedVariable(variable: VariableDeclaration): boolean {
  const initializer = variable.getInitializer();
  return initializer?.isKind(SyntaxKind.ArrowFunction) === true
    || initializer?.isKind(SyntaxKind.FunctionExpression) === true;
}

function resolveHeritageTargetId(
  projectName: string,
  rootPath: string,
  heritageClause: Node,
  knownIds: Set<string>,
): string | undefined {
  const expressionText = getHeritageExpressionText(heritageClause);
  const symbol = getHeritageExpressionSymbol(heritageClause);
  const resolved = resolveSymbolTargetId(projectName, rootPath, symbol, expressionText.split('<')[0], knownIds);
  if (resolved) return resolved;

  const fallbackName = expressionText.split('<')[0];
  return findInterfaceId(fallbackName, projectName, knownIds) ?? resolveClassIdByName(projectName, rootPath, fallbackName, knownIds);
}

function resolveClassIdByName(
  projectName: string,
  _rootPath: string,
  name: string,
  knownIds: Set<string>,
): string | undefined {
  for (const id of knownIds) {
    if (id.startsWith(`${projectName}:Class:`) && id.endsWith(`:${name}`)) {
      return id;
    }
  }
  return undefined;
}

function getHeritageExpressionText(node: Node): string {
  if ('getExpression' in node && typeof node.getExpression === 'function') {
    return node.getExpression().getText();
  }
  return node.getText();
}

function getHeritageExpressionSymbol(node: Node): TsSymbol | undefined {
  if ('getExpression' in node && typeof node.getExpression === 'function') {
    return node.getExpression().getSymbol();
  }
  return node.getSymbol();
}

function sanitizeNamespace(value: string): string {
  return value.replace(/[\\/:*?"<>|]/g, '_');
}
