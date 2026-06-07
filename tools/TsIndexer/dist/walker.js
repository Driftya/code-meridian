import path from 'path';
import fs from 'fs';
import { createHash } from 'crypto';
import { Project, SyntaxKind } from 'ts-morph';
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
    const fId = fileId(projectName, relPath);
    addNode(nodes, knownIds, {
        id: fId,
        name: path.basename(relPath),
        type: 'File',
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
        }
        for (const prop of cls.getProperties()) {
            const pId = nodeId(projectName, relPath, `${name}.${prop.getName()}`, 'Property');
            if (knownIds.has(pId))
                edges.push({ sourceId: cId, targetId: pId, type: 'Contains' });
        }
        // Inherits — only within indexed project
        const baseClass = cls.getBaseClass();
        if (baseClass) {
            const baseName = baseClass.getName();
            if (baseName) {
                const baseSourceFile = baseClass.getSourceFile();
                const baseRelPath = path.relative(rootPath, baseSourceFile.getFilePath()).replace(/\\/g, '/');
                const baseId = nodeId(projectName, baseRelPath, baseName, 'Class');
                if (knownIds.has(baseId))
                    edges.push({ sourceId: cId, targetId: baseId, type: 'Inherits' });
            }
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
        }
        // Interface extends interface
        for (const ext of iface.getExtends()) {
            const extName = ext.getExpression().getText().split('<')[0];
            const matchingId = findInterfaceId(extName, projectName, knownIds);
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
