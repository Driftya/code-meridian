import path from 'path';
import fs from 'fs';
import { createHash } from 'crypto';
import { Project, SyntaxKind, } from 'ts-morph';
// ── ID helpers ────────────────────────────────────────────────────────────────
function sanitize(s) {
    return s.replace(/[\\/:*?"<>|]/g, '_');
}
function fileId(project, relPath) {
    return `${project}:File:${sanitize(relPath)}`;
}
function nodeId(project, relPath, name, type) {
    return `${project}:${type}:${sanitize(relPath)}:${name}`;
}
// ── Main entry ────────────────────────────────────────────────────────────────
export function walkTypeScript(rootPath, projectName, files) {
    const nodes = [];
    const edges = [];
    const knownIds = new Set();
    const methodIndex = new Map();
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
    }
    else {
        tsProject.addSourceFilesAtPaths([
            path.join(rootPath, '**/*.ts').replace(/\\/g, '/'),
            path.join(rootPath, '**/*.tsx').replace(/\\/g, '/'),
            `!${path.join(rootPath, '**/node_modules/**').replace(/\\/g, '/')}`,
            `!${path.join(rootPath, '**/dist/**').replace(/\\/g, '/')}`,
            `!${path.join(rootPath, '**/build/**').replace(/\\/g, '/')}`,
            `!${path.join(rootPath, '**/*.d.ts').replace(/\\/g, '/')}`,
        ]);
    }
    const sourceFiles = tsProject.getSourceFiles();
    // First pass: collect all nodes so we can validate edge targets
    for (const sourceFile of sourceFiles) {
        collectNodes(sourceFile, rootPath, projectName, nodes, knownIds);
    }
    indexMethods(nodes, methodIndex);
    // Second pass: collect edges (only emit where target is known or is a local file)
    for (const sourceFile of sourceFiles) {
        collectEdges(sourceFile, rootPath, projectName, nodes, edges, knownIds, methodIndex);
    }
    return { nodes, edges };
}
// ── Node collection ───────────────────────────────────────────────────────────
function collectNodes(sourceFile, rootPath, projectName, nodes, knownIds) {
    const relPath = path.relative(rootPath, sourceFile.getFilePath()).replace(/\\/g, '/');
    const namespace = getNamespaceForPath(relPath, isTestFilePath(relPath));
    const fId = fileId(projectName, relPath);
    if (namespace) {
        const moduleId = `${projectName}:Module:${sanitize(namespace)}`;
        addNode(nodes, knownIds, {
            id: moduleId,
            name: namespace,
            type: 'Module',
            namespace,
            filePath: relPath,
            lineNumber: 1,
            lineCount: sourceFile.getEndLineNumber(),
            projectContext: projectName,
        });
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
    });
    // Classes
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
        });
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
            });
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
            namespace,
            filePath: relPath,
            lineNumber: iface.getStartLineNumber(),
            lineCount: iface.getEndLineNumber() - iface.getStartLineNumber() + 1,
            sourceSnippet: sourceSnippet(sourceFile, iface.getStartLineNumber(), iface.getEndLineNumber()),
            sourceHash: sourceHash(sourceFile, iface.getStartLineNumber(), iface.getEndLineNumber()),
            projectContext: projectName,
        });
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
            namespace,
            filePath: relPath,
            lineNumber: fn.getStartLineNumber(),
            lineCount: fn.getEndLineNumber() - fn.getStartLineNumber() + 1,
            sourceSnippet: sourceSnippet(sourceFile, fn.getStartLineNumber(), fn.getEndLineNumber()),
            sourceHash: sourceHash(sourceFile, fn.getStartLineNumber(), fn.getEndLineNumber()),
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
            namespace,
            filePath: relPath,
            lineNumber: enumDecl.getStartLineNumber(),
            lineCount: enumDecl.getEndLineNumber() - enumDecl.getStartLineNumber() + 1,
            sourceSnippet: sourceSnippet(sourceFile, enumDecl.getStartLineNumber(), enumDecl.getEndLineNumber()),
            sourceHash: sourceHash(sourceFile, enumDecl.getStartLineNumber(), enumDecl.getEndLineNumber()),
            projectContext: projectName,
        });
    }
}
// ── Edge collection ───────────────────────────────────────────────────────────
function collectEdges(sourceFile, rootPath, projectName, nodes, edges, knownIds, methodIndex) {
    const relPath = path.relative(rootPath, sourceFile.getFilePath()).replace(/\\/g, '/');
    const fId = fileId(projectName, relPath);
    // File → Class/Interface/Enum/Function (Contains)
    for (const cls of sourceFile.getClasses()) {
        const name = cls.getName() ?? '<anonymous>';
        const cId = nodeId(projectName, relPath, name, 'Class');
        if (knownIds.has(cId))
            edges.push({ sourceId: fId, targetId: cId, type: 'Contains' });
        for (const method of cls.getMethods()) {
            const mId = nodeId(projectName, relPath, `${name}.${method.getName()}`, 'Method');
            if (knownIds.has(mId))
                edges.push({ sourceId: cId, targetId: mId, type: 'Contains' });
            addCallEdges(mId, method.getDescendantsOfKind(SyntaxKind.CallExpression), edges, methodIndex, {
                filePath: relPath,
                className: name,
            });
            addTypeUseEdges(projectName, rootPath, relPath, method, mId, edges, knownIds);
        }
        for (const prop of cls.getProperties()) {
            const pId = nodeId(projectName, relPath, `${name}.${prop.getName()}`, 'Property');
            if (knownIds.has(pId))
                edges.push({ sourceId: cId, targetId: pId, type: 'Contains' });
            addTypeUseEdges(projectName, rootPath, relPath, prop, pId, edges, knownIds);
        }
        // Inherits — only within indexed project
        const baseClass = cls.getBaseClass();
        if (baseClass) {
            const baseId = resolveHeritageTargetId(projectName, rootPath, baseClass, knownIds);
            if (baseId)
                edges.push({ sourceId: cId, targetId: baseId, type: 'Inherits' });
        }
        // Implements — only within indexed project
        for (const impl of cls.getImplements()) {
            const ifaceName = impl.getExpression().getText().split('<')[0]; // strip generics
            // Try to find the interface in any indexed file
            const matchingId = findInterfaceId(ifaceName, projectName, knownIds);
            if (matchingId)
                edges.push({ sourceId: cId, targetId: matchingId, type: 'Implements' });
        }
    }
    for (const iface of sourceFile.getInterfaces()) {
        const name = iface.getName();
        const iId = nodeId(projectName, relPath, name, 'Interface');
        if (knownIds.has(iId))
            edges.push({ sourceId: fId, targetId: iId, type: 'Contains' });
        for (const method of iface.getMethods()) {
            const mId = nodeId(projectName, relPath, `${name}.${method.getName()}`, 'Method');
            if (knownIds.has(mId))
                edges.push({ sourceId: iId, targetId: mId, type: 'Contains' });
            addTypeUseEdges(projectName, rootPath, relPath, method, mId, edges, knownIds);
        }
        // Interface extends interface
        for (const ext of iface.getExtends()) {
            const matchingId = resolveHeritageTargetId(projectName, rootPath, ext, knownIds);
            if (matchingId)
                edges.push({ sourceId: iId, targetId: matchingId, type: 'Inherits' });
        }
    }
    for (const fn of sourceFile.getFunctions()) {
        const name = fn.getName() ?? '<anonymous>';
        const fnId = nodeId(projectName, relPath, name, 'Method');
        if (knownIds.has(fnId))
            edges.push({ sourceId: fId, targetId: fnId, type: 'Contains' });
        addCallEdges(fnId, fn.getDescendantsOfKind(SyntaxKind.CallExpression), edges, methodIndex, {
            filePath: relPath,
        });
        addTypeUseEdges(projectName, rootPath, relPath, fn, fnId, edges, knownIds);
    }
    for (const enumDecl of sourceFile.getEnums()) {
        const eId = nodeId(projectName, relPath, enumDecl.getName(), 'Enum');
        if (knownIds.has(eId))
            edges.push({ sourceId: fId, targetId: eId, type: 'Contains' });
    }
    // Local import dependencies (File DependsOn File)
    for (const importDecl of sourceFile.getImportDeclarations()) {
        if (!importDecl.getModuleSpecifierValue().startsWith('.'))
            continue;
        const resolved = importDecl.getModuleSpecifierSourceFile();
        if (!resolved)
            continue;
        const targetRelPath = path.relative(rootPath, resolved.getFilePath()).replace(/\\/g, '/');
        const targetId = fileId(projectName, targetRelPath);
        if (knownIds.has(targetId))
            edges.push({ sourceId: fId, targetId: targetId, type: 'DependsOn' });
        const importClause = importDecl.getImportClause();
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
// ── Helpers ───────────────────────────────────────────────────────────────────
function addNode(nodes, knownIds, node) {
    if (!knownIds.has(node.id)) {
        knownIds.add(node.id);
        nodes.push(node);
    }
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
function addCallEdges(sourceId, calls, edges, methodIndex, source) {
    for (const call of calls) {
        const calleeName = calleeShortName(call);
        if (!calleeName)
            continue;
        const candidates = methodIndex.get(calleeName) ?? [];
        const targetId = selectCallTarget(candidates, source, calleeName);
        if (!targetId || targetId === sourceId)
            continue;
        edges.push({ sourceId, targetId, type: 'Calls' });
    }
}
function selectCallTarget(candidates, source, calleeName) {
    if (candidates.length === 0)
        return undefined;
    if (candidates.length === 1)
        return candidates[0];
    if (source.className) {
        const sameClass = candidates.filter(id => id.endsWith(`:${source.className}.${calleeName}`));
        if (sameClass.length === 1)
            return sameClass[0];
    }
    const sameFileToken = sanitize(source.filePath);
    const sameFile = candidates.filter(id => id.includes(`:${sameFileToken}:`));
    return sameFile.length === 1 ? sameFile[0] : undefined;
}
function calleeShortName(call) {
    const expression = call.getExpression().getText();
    const match = /([A-Za-z_$][\w$]*)\s*$/.exec(expression.split('<')[0]);
    return match?.[1];
}
function methodShortName(name) {
    const withoutParams = name.split('(')[0];
    const segments = withoutParams.split('.');
    return segments[segments.length - 1] ?? withoutParams;
}
function sourceSnippet(sourceFile, startLine, endLine) {
    const maxLines = 80;
    const maxChars = 12_000;
    const lines = sourceFile.getFullText().split(/\r?\n/);
    const selected = lines.slice(Math.max(0, startLine - 1), Math.min(endLine, startLine - 1 + maxLines));
    const snippet = selected.join('\n').trimEnd();
    if (!snippet.trim())
        return undefined;
    return snippet.length > maxChars ? snippet.slice(0, maxChars) : snippet;
}
function sourceHash(sourceFile, startLine, endLine) {
    const lines = sourceFile.getFullText().split(/\r?\n/);
    return hashText(lines.slice(Math.max(0, startLine - 1), endLine).join('\n'));
}
function hashText(text) {
    return createHash('sha256').update(text, 'utf8').digest('hex');
}
/** Scan knownIds for an Interface node matching the given short name. */
function findInterfaceId(shortName, projectName, knownIds) {
    const suffix = `:Interface:`;
    for (const id of knownIds) {
        if (id.startsWith(`${projectName}${suffix}`) && id.endsWith(`:${shortName}`)) {
            return id;
        }
    }
    return undefined;
}
function addTypeUseEdges(projectName, rootPath, relPath, node, sourceId, edges, knownIds) {
    const typeReferences = node.getDescendantsOfKind(SyntaxKind.TypeReference);
    for (const typeRef of typeReferences) {
        const targetId = resolveTypeReference(projectName, rootPath, relPath, typeRef, knownIds);
        if (targetId && targetId !== sourceId) {
            edges.push({ sourceId, targetId, type: 'Uses' });
        }
    }
}
function resolveTypeReference(projectName, rootPath, relPath, typeRef, knownIds) {
    const symbol = typeRef.getTypeName().getSymbol();
    return resolveSymbolTargetId(projectName, rootPath, symbol, typeRef.getText(), knownIds);
}
function resolveExportedTypeNodeId(projectName, rootPath, sourceFile, exportedName, knownIds) {
    const relPath = path.relative(rootPath, sourceFile.getFilePath()).replace(/\\/g, '/');
    for (const type of ['Class', 'Interface', 'Enum']) {
        const targetId = nodeId(projectName, relPath, exportedName, type);
        if (knownIds.has(targetId))
            return targetId;
    }
    for (const exportDecl of sourceFile.getExportDeclarations()) {
        const namedExports = exportDecl.getNamedExports();
        for (const namedExport of namedExports) {
            const alias = namedExport.getAliasNode()?.getText();
            const name = namedExport.getNameNode().getText();
            if (alias !== exportedName && name !== exportedName)
                continue;
            const resolved = exportDecl.getModuleSpecifierSourceFile();
            if (!resolved)
                continue;
            const resolvedId = resolveExportedTypeNodeId(projectName, rootPath, resolved, name, knownIds);
            if (resolvedId)
                return resolvedId;
        }
    }
    return undefined;
}
function getNamespaceForPath(relPath, isTestFile) {
    const dir = path.posix.dirname(relPath.replace(/\\/g, '/'));
    const namespace = dir === '.' ? undefined : dir;
    if (!isTestFile)
        return namespace;
    return namespace ? `test/${namespace}` : 'test';
}
function resolveSymbolTargetId(projectName, rootPath, symbol, fallbackName, knownIds) {
    const visited = new Set();
    return resolveSymbolTargetIdCore(projectName, rootPath, symbol, fallbackName, knownIds, visited);
}
function resolveSymbolTargetIdCore(projectName, rootPath, symbol, fallbackName, knownIds, visited) {
    if (!symbol)
        return undefined;
    const key = symbol.getFullyQualifiedName();
    if (visited.has(key))
        return undefined;
    visited.add(key);
    const aliased = symbol.getAliasedSymbol?.();
    if (aliased && aliased !== symbol) {
        const resolved = resolveSymbolTargetIdCore(projectName, rootPath, aliased, fallbackName, knownIds, visited);
        if (resolved)
            return resolved;
    }
    for (const declaration of symbol.getDeclarations()) {
        const declarationId = resolveDeclarationTargetId(projectName, rootPath, declaration, knownIds);
        if (declarationId)
            return declarationId;
    }
    const sourceDeclarations = symbol.getDeclarations();
    if (sourceDeclarations.length > 0) {
        const first = sourceDeclarations[0];
        const relPath = path.relative(rootPath, first.getSourceFile().getFilePath()).replace(/\\/g, '/');
        for (const type of ['Class', 'Interface', 'Enum']) {
            const targetId = nodeId(projectName, relPath, fallbackName, type);
            if (knownIds.has(targetId))
                return targetId;
        }
    }
    return undefined;
}
function resolveDeclarationTargetId(projectName, rootPath, declaration, knownIds) {
    const sourceFile = declaration.getSourceFile();
    const relPath = path.relative(rootPath, sourceFile.getFilePath()).replace(/\\/g, '/');
    const name = declaration.getSymbol()?.getName() ?? declaration.getText().split(/\s+/)[0];
    const kind = declaration.getKindName();
    const targetType = kind === 'InterfaceDeclaration'
        ? 'Interface'
        : kind === 'ClassDeclaration'
            ? 'Class'
            : kind === 'EnumDeclaration'
                ? 'Enum'
                : kind === 'TypeAliasDeclaration'
                    ? 'Interface'
                    : undefined;
    if (!targetType)
        return undefined;
    const targetId = nodeId(projectName, relPath, name, targetType);
    return knownIds.has(targetId) ? targetId : undefined;
}
function resolveHeritageTargetId(projectName, rootPath, heritageClause, knownIds) {
    const expressionText = getHeritageExpressionText(heritageClause);
    const symbol = getHeritageExpressionSymbol(heritageClause);
    const resolved = resolveSymbolTargetId(projectName, rootPath, symbol, expressionText.split('<')[0], knownIds);
    if (resolved)
        return resolved;
    const fallbackName = expressionText.split('<')[0];
    return findInterfaceId(fallbackName, projectName, knownIds) ?? resolveClassIdByName(projectName, rootPath, fallbackName, knownIds);
}
function resolveClassIdByName(projectName, rootPath, name, knownIds) {
    for (const id of knownIds) {
        if (id.startsWith(`${projectName}:Class:`) && id.endsWith(`:${name}`)) {
            return id;
        }
    }
    return undefined;
}
function isTestFilePath(relPath) {
    const normalized = relPath.replace(/\\/g, '/').toLowerCase();
    return normalized.includes('/test/') ||
        normalized.includes('/tests/') ||
        normalized.includes('/__tests__/') ||
        normalized.includes('.test.') ||
        normalized.includes('.spec.');
}
function getHeritageExpressionText(node) {
    if ('getExpression' in node && typeof node.getExpression === 'function') {
        return node.getExpression().getText();
    }
    return node.getText();
}
function getHeritageExpressionSymbol(node) {
    if ('getExpression' in node && typeof node.getExpression === 'function') {
        return node.getExpression().getSymbol();
    }
    return node.getSymbol();
}
