import path from 'node:path';
import { createHash } from 'node:crypto';
import { SyntaxKind, type Node, type SourceFile } from 'ts-morph';
import type { CodeNodeDto } from '../types.js';

export function sanitize(value: string): string {
  return value.replace(/[\\/:*?"<>|]/g, '_');
}

export function fileId(project: string, relPath: string): string {
  return `${project}:File:${sanitize(relPath)}`;
}

export function nodeId(project: string, relPath: string, name: string, type: string): string {
  return `${project}:${type}:${sanitize(relPath)}:${name}`;
}

export function addNode(
  nodes: CodeNodeDto[],
  knownIds: Set<string>,
  node: CodeNodeDto,
  classifyFileRole?: (relativePath: string) => string | undefined,
): void {
  if (!knownIds.has(node.id)) {
    if (!node.fileRole && node.filePath && classifyFileRole) {
      node.fileRole = classifyFileRole(node.filePath);
    }
    knownIds.add(node.id);
    nodes.push(node);
  }
}

export function sourceSnippet(sourceFile: SourceFile, startLine: number, endLine: number): string | undefined {
  const maxLines = 80;
  const maxChars = 12_000;
  const lines = sourceFile.getFullText().split(/\r?\n/);
  const selected = lines.slice(Math.max(0, startLine - 1), Math.min(endLine, startLine - 1 + maxLines));
  const snippet = selected.join('\n').trimEnd();
  if (!snippet.trim()) return undefined;
  return snippet.length > maxChars ? snippet.slice(0, maxChars) : snippet;
}

export function sourceHash(sourceFile: SourceFile, startLine: number, endLine: number): string {
  const lines = sourceFile.getFullText().split(/\r?\n/);
  return hashText(lines.slice(Math.max(0, startLine - 1), endLine).join('\n'));
}

export function hashText(text: string): string {
  return createHash('sha256').update(text, 'utf8').digest('hex');
}

export function resolveStringLiteralValue(node: Node): string | undefined {
  if (node.getKind() === SyntaxKind.StringLiteral) {
    return node.asKindOrThrow(SyntaxKind.StringLiteral).getLiteralValue();
  }

  if (node.getKind() === SyntaxKind.NoSubstitutionTemplateLiteral) {
    return node.asKindOrThrow(SyntaxKind.NoSubstitutionTemplateLiteral).getLiteralText();
  }

  return undefined;
}

export function getNamespaceForPath(relPath: string, isTestFile: boolean): string | undefined {
  const dir = path.posix.dirname(relPath.replace(/\\/g, '/'));
  const namespace = dir === '.' ? undefined : dir;
  if (!isTestFile) return namespace;
  return namespace ? `test/${namespace}` : 'test';
}

export function isTestFilePath(relPath: string): boolean {
  const normalized = relPath.replace(/\\/g, '/').toLowerCase();
  return normalized.includes('/test/') ||
    normalized.includes('/tests/') ||
    normalized.includes('/__tests__/') ||
    normalized.includes('.test.') ||
    normalized.includes('.spec.');
}

export function buildSyntheticTestCaseName(testInvoker: 'it' | 'test', label: string, lineNumber: number): string {
  const normalizedLabel = label.replace(/\s+/g, ' ').trim();
  return `__testcase__.${testInvoker}.${normalizedLabel}@L${lineNumber}`;
}

export function syntheticTestMethodId(projectName: string, relPath: string, name: string): string {
  return `${projectName}:Method:${sanitize(relPath)}:${sanitize(name)}`;
}
