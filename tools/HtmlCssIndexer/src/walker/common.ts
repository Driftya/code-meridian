import { createHash } from 'node:crypto';
import path from 'node:path';
import type { CodeNodeDto } from '@codemeridian/indexer-shared';
import type { FrontendConceptKind } from '../types.js';

export function sanitize(value: string): string {
  return value.replace(/[\\/:*?"<>|]/g, '_');
}

export function toRelativePath(rootPath: string, filePath: string): string {
  return path.relative(rootPath, filePath).replace(/\\/g, '/');
}

export function fileNodeId(projectName: string, relativePath: string): string {
  return `${projectName}:File:${sanitize(relativePath)}`;
}

export function frontendConceptId(projectName: string, kind: FrontendConceptKind, name: string): string {
  return `${projectName}:ExternalConcept:${kind}:${sanitize(name)}`;
}

export function selectorNodeId(projectName: string, relativePath: string, selectorText: string, lineNumber: number): string {
  return `${projectName}:ExternalConcept:${sanitize(relativePath)}:CssSelector:${lineNumber}:${sanitize(selectorText)}`;
}

export function addNode(
  nodes: CodeNodeDto[],
  knownIds: Set<string>,
  node: CodeNodeDto,
  resolveFileRole?: (relativePath: string) => string | undefined,
): void {
  if (knownIds.has(node.id))
    return;

  if (!node.fileRole && node.filePath && resolveFileRole) {
    node.fileRole = resolveFileRole(node.filePath);
  }

  knownIds.add(node.id);
  nodes.push(node);
}

export function hashText(text: string): string {
  return createHash('sha256').update(text, 'utf8').digest('hex');
}

export function lineCountFromContent(content: string): number {
  return content.length === 0 ? 0 : content.split(/\r?\n/).length;
}

export function lineNumberAt(content: string, index: number): number {
  return content.slice(0, Math.max(0, index)).split(/\r?\n/).length;
}

export function splitClassNames(value: string): string[] {
  return value
    .split(/\s+/)
    .map(token => token.trim())
    .filter(token => token.length > 0);
}

export function normalizeImportTarget(value: string): string | undefined {
  const normalized = value.trim().replace(/^['"]|['"]$/g, '');
  return normalized.length > 0 ? normalized : undefined;
}
