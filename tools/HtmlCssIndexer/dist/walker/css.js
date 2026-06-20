import fs from 'node:fs';
import path from 'node:path';
import postcss from 'postcss';
import postcssScss from 'postcss-scss';
import { addNode, fileNodeId, frontendConceptId, hashText, lineCountFromContent, normalizeImportTarget, selectorNodeId, styleDeclarationNodeId, toRelativePath } from './common.js';
const CLASS_SELECTOR = /\.([_a-zA-Z][\w-]*)/g;
const ID_SELECTOR = /#([_a-zA-Z][\w-]*)/g;
const CSS_VARIABLE = /var\(\s*(--[\w-]+)\s*(?:,[^)]+)?\)/g;
const STYLE_IMPORT = /(?:url\()?["']([^"']+\.(?:css|scss|sass))(?:["']\))?/i;
export function collectStyleArtifacts(rootPath, projectName, filePath, nodes, edges, knownIds, resolveFileRole) {
    const relativePath = toRelativePath(rootPath, filePath);
    const content = fs.readFileSync(filePath, 'utf8');
    const fileId = fileNodeId(projectName, relativePath);
    addNode(nodes, knownIds, {
        id: fileId,
        name: path.posix.basename(relativePath),
        type: 'File',
        filePath: relativePath,
        lineNumber: 1,
        lineCount: lineCountFromContent(content),
        summary: 'HTML/CSS/SCSS indexer stylesheet file.',
        sourceHash: hashText(content),
        projectContext: projectName,
        properties: {
            language: relativePath.endsWith('.scss') ? 'scss' : 'css',
            frontendRole: 'StyleSheetFile',
        },
    }, resolveFileRole);
    const root = relativePath.endsWith('.scss')
        ? postcssScss.parse(content, { from: filePath })
        : postcss.parse(content, { from: filePath });
    root.walkAtRules(atRule => {
        if (!['import', 'use', 'forward'].includes(atRule.name))
            return;
        const target = normalizeImportTarget(atRule.params.match(STYLE_IMPORT)?.[1] ?? atRule.params);
        if (!target)
            return;
        const importedPath = resolveImportedStylePath(path.dirname(filePath), target);
        if (!importedPath)
            return;
        const importedRelativePath = toRelativePath(rootPath, importedPath);
        addNode(nodes, knownIds, {
            id: fileNodeId(projectName, importedRelativePath),
            name: path.posix.basename(importedRelativePath),
            type: 'File',
            filePath: importedRelativePath,
            projectContext: projectName,
            properties: {
                frontendRole: 'StyleSheetFile',
            },
        }, resolveFileRole);
        edges.push({
            sourceId: fileId,
            targetId: fileNodeId(projectName, importedRelativePath),
            type: 'ImportsStyle',
            callSite: `${relativePath}:${atRule.source?.start?.line ?? 1}`,
        });
    });
    root.walkRules(rule => {
        const selectorText = rule.selector?.trim();
        if (!selectorText)
            return;
        const lineNumber = rule.source?.start?.line ?? 1;
        const selectorId = selectorNodeId(projectName, relativePath, selectorText, lineNumber);
        addNode(nodes, knownIds, {
            id: selectorId,
            name: selectorText,
            type: 'ExternalConcept',
            filePath: relativePath,
            lineNumber,
            lineCount: 1,
            projectContext: projectName,
            properties: {
                externalKind: 'CssSelector',
                selectorText,
            },
        }, resolveFileRole);
        edges.push({
            sourceId: fileId,
            targetId: selectorId,
            type: 'DefinesSelector',
            callSite: `${relativePath}:${lineNumber}`,
        });
        for (const className of extractAll(selectorText, CLASS_SELECTOR)) {
            const classId = frontendConceptId(projectName, 'CssClass', className);
            addNode(nodes, knownIds, {
                id: classId,
                name: className,
                type: 'ExternalConcept',
                projectContext: projectName,
                properties: {
                    externalKind: 'CssClass',
                },
            });
            edges.push({
                sourceId: selectorId,
                targetId: classId,
                type: 'UsesClass',
                callSite: `${relativePath}:${lineNumber}`,
            });
        }
        for (const idValue of extractAll(selectorText, ID_SELECTOR)) {
            const idNodeId = frontendConceptId(projectName, 'CssId', idValue);
            addNode(nodes, knownIds, {
                id: idNodeId,
                name: idValue,
                type: 'ExternalConcept',
                projectContext: projectName,
                properties: {
                    externalKind: 'CssId',
                },
            });
            edges.push({
                sourceId: selectorId,
                targetId: idNodeId,
                type: 'UsesId',
                callSite: `${relativePath}:${lineNumber}`,
            });
        }
        rule.walkDecls(decl => {
            const declarationLine = decl.source?.start?.line ?? lineNumber;
            if (decl.prop.startsWith('--')) {
                const variableId = frontendConceptId(projectName, 'CssVariable', decl.prop);
                addNode(nodes, knownIds, {
                    id: variableId,
                    name: decl.prop,
                    type: 'ExternalConcept',
                    projectContext: projectName,
                    properties: {
                        externalKind: 'CssVariable',
                        rawValue: decl.value,
                    },
                });
                edges.push({
                    sourceId: fileId,
                    targetId: variableId,
                    type: 'DefinesCssVariable',
                    callSite: `${relativePath}:${declarationLine}`,
                });
            }
            const declarationId = styleDeclarationNodeId(projectName, relativePath, selectorText, decl.prop, declarationLine, decl.value);
            addNode(nodes, knownIds, {
                id: declarationId,
                name: `${decl.prop}: ${decl.value}`,
                type: 'ExternalConcept',
                filePath: relativePath,
                lineNumber: declarationLine,
                lineCount: 1,
                projectContext: projectName,
                properties: {
                    externalKind: 'CssDeclaration',
                    selectorText,
                    propertyName: decl.prop,
                    rawValue: decl.value,
                },
            }, resolveFileRole);
            edges.push({
                sourceId: selectorId,
                targetId: declarationId,
                type: 'Uses',
                callSite: `${relativePath}:${declarationLine}`,
                properties: {
                    relationshipKind: 'DefinesStyleDeclaration',
                },
            });
            for (const variableName of extractAll(decl.value, CSS_VARIABLE)) {
                const variableId = frontendConceptId(projectName, 'CssVariable', variableName);
                addNode(nodes, knownIds, {
                    id: variableId,
                    name: variableName,
                    type: 'ExternalConcept',
                    projectContext: projectName,
                    properties: {
                        externalKind: 'CssVariable',
                    },
                });
                edges.push({
                    sourceId: selectorId,
                    targetId: variableId,
                    type: 'UsesCssVariable',
                    callSite: `${relativePath}:${declarationLine}`,
                });
            }
        });
    });
}
function extractAll(value, pattern) {
    const matches = new Set();
    pattern.lastIndex = 0;
    let match;
    while ((match = pattern.exec(value)) !== null) {
        if (match[1])
            matches.add(match[1]);
    }
    return [...matches];
}
function resolveImportedStylePath(baseDirectory, target) {
    if (!target.startsWith('.'))
        return undefined;
    const candidates = [target];
    if (!path.extname(target)) {
        candidates.push(`${target}.css`, `${target}.scss`);
    }
    for (const candidate of candidates) {
        const fullPath = path.resolve(baseDirectory, candidate);
        if (fs.existsSync(fullPath))
            return fullPath;
    }
    return undefined;
}
