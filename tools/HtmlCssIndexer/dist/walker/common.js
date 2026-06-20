import { createHash } from 'node:crypto';
import path from 'node:path';
export function sanitize(value) {
    return value.replace(/[\\/:*?"<>|]/g, '_');
}
export function toRelativePath(rootPath, filePath) {
    return path.relative(rootPath, filePath).replace(/\\/g, '/');
}
export function fileNodeId(projectName, relativePath) {
    return `${projectName}:File:${sanitize(relativePath)}`;
}
export function frontendConceptId(projectName, kind, name) {
    return `${projectName}:ExternalConcept:${kind}:${sanitize(name)}`;
}
export function selectorNodeId(projectName, relativePath, selectorText, lineNumber) {
    return `${projectName}:ExternalConcept:${sanitize(relativePath)}:CssSelector:${lineNumber}:${sanitize(selectorText)}`;
}
export function styleDeclarationNodeId(projectName, relativePath, selectorText, propertyName, lineNumber, rawValue) {
    const fingerprint = hashText(`${selectorText}|${propertyName}|${rawValue}`).slice(0, 12);
    return `${projectName}:ExternalConcept:${sanitize(relativePath)}:CssDeclaration:${lineNumber}:${sanitize(propertyName)}:${fingerprint}`;
}
export function addNode(nodes, knownIds, node, resolveFileRole) {
    if (knownIds.has(node.id))
        return;
    if (!node.fileRole && node.filePath && resolveFileRole) {
        node.fileRole = resolveFileRole(node.filePath);
    }
    knownIds.add(node.id);
    nodes.push(node);
}
export function hashText(text) {
    return createHash('sha256').update(text, 'utf8').digest('hex');
}
export function lineCountFromContent(content) {
    return content.length === 0 ? 0 : content.split(/\r?\n/).length;
}
export function lineNumberAt(content, index) {
    return content.slice(0, Math.max(0, index)).split(/\r?\n/).length;
}
export function splitClassNames(value) {
    return value
        .split(/\s+/)
        .map(token => token.trim())
        .filter(token => token.length > 0);
}
export function normalizeImportTarget(value) {
    const normalized = value.trim().replace(/^['"]|['"]$/g, '');
    return normalized.length > 0 ? normalized : undefined;
}
