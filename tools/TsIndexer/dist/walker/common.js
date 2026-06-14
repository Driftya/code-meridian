import path from 'node:path';
import { createHash } from 'node:crypto';
import { SyntaxKind } from 'ts-morph';
export function sanitize(value) {
    return value.replace(/[\\/:*?"<>|]/g, '_');
}
export function fileId(project, relPath) {
    return `${project}:File:${sanitize(relPath)}`;
}
export function nodeId(project, relPath, name, type) {
    return `${project}:${type}:${sanitize(relPath)}:${name}`;
}
export function addNode(nodes, knownIds, node, classifyFileRole) {
    if (!knownIds.has(node.id)) {
        if (!node.fileRole && node.filePath && classifyFileRole) {
            node.fileRole = classifyFileRole(node.filePath);
        }
        knownIds.add(node.id);
        nodes.push(node);
    }
}
export function sourceSnippet(sourceFile, startLine, endLine) {
    const maxLines = 80;
    const maxChars = 12_000;
    const lines = sourceFile.getFullText().split(/\r?\n/);
    const selected = lines.slice(Math.max(0, startLine - 1), Math.min(endLine, startLine - 1 + maxLines));
    const snippet = selected.join('\n').trimEnd();
    if (!snippet.trim())
        return undefined;
    return snippet.length > maxChars ? snippet.slice(0, maxChars) : snippet;
}
export function sourceHash(sourceFile, startLine, endLine) {
    const lines = sourceFile.getFullText().split(/\r?\n/);
    return hashText(lines.slice(Math.max(0, startLine - 1), endLine).join('\n'));
}
export function hashText(text) {
    return createHash('sha256').update(text, 'utf8').digest('hex');
}
export function resolveStringLiteralValue(node) {
    if (node.getKind() === SyntaxKind.StringLiteral) {
        return node.asKindOrThrow(SyntaxKind.StringLiteral).getLiteralValue();
    }
    if (node.getKind() === SyntaxKind.NoSubstitutionTemplateLiteral) {
        return node.asKindOrThrow(SyntaxKind.NoSubstitutionTemplateLiteral).getLiteralText();
    }
    return undefined;
}
export function getNamespaceForPath(relPath, isTestFile) {
    const dir = path.posix.dirname(relPath.replace(/\\/g, '/'));
    const namespace = dir === '.' ? undefined : dir;
    if (!isTestFile)
        return namespace;
    return namespace ? `test/${namespace}` : 'test';
}
export function isTestFilePath(relPath) {
    const normalized = relPath.replace(/\\/g, '/').toLowerCase();
    return normalized.includes('/test/') ||
        normalized.includes('/tests/') ||
        normalized.includes('/__tests__/') ||
        normalized.includes('.test.') ||
        normalized.includes('.spec.');
}
export function buildSyntheticTestCaseName(testInvoker, label, lineNumber) {
    const normalizedLabel = label.replace(/\s+/g, ' ').trim();
    return `__testcase__.${testInvoker}.${normalizedLabel}@L${lineNumber}`;
}
export function syntheticTestMethodId(projectName, relPath, name) {
    return `${projectName}:Method:${sanitize(relPath)}:${sanitize(name)}`;
}
