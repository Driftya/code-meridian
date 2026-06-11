import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import {
  containsFile,
  findTypeScriptRoots,
  isDocumentationFile,
  isIgnoredPath,
  isTypeScriptSourceFile,
  resolveProjectName,
} from '../src/services/project-discovery.js';

let rootPath: string;

beforeEach(() => {
  rootPath = fs.mkdtempSync(path.join(os.tmpdir(), 'codemeridian-ts-discovery-'));
});

afterEach(() => {
  fs.rmSync(rootPath, { recursive: true, force: true });
});

describe('project discovery', () => {
  it('resolves project name from package json first', () => {
    writeFile('package.json', '{"name":"my-web"}');
    writeFile('Other.code-workspace', '');

    expect(resolveProjectName(rootPath)).toBe('my-web');
  });

  it('detects ignored paths and ts source files', () => {
    expect(isTypeScriptSourceFile(path.join(rootPath, 'index.ts'))).toBe(true);
    expect(isTypeScriptSourceFile(path.join(rootPath, 'types.d.ts'))).toBe(false);

    const ignoredPath = path.join(rootPath, 'node_modules', 'left-pad', 'index.ts');
    expect(isIgnoredPath(rootPath, ignoredPath)).toBe(true);
  });

  it('detects documentation files', () => {
    expect(isDocumentationFile(path.join(rootPath, 'README.md'))).toBe(true);
    expect(isDocumentationFile(path.join(rootPath, 'notes.txt'))).toBe(true);
    expect(isDocumentationFile(path.join(rootPath, 'index.ts'))).toBe(false);
  });

  it('finds nearest tsconfig root', () => {
    writeFile('apps/web/tsconfig.json', '{}');
    writeFile('apps/web/src/index.ts', 'export const app = true;');
    writeFile('apps/web/src/nested/tsconfig.json', '{}');
    writeFile('apps/web/src/nested/feature.ts', 'export const feature = true;');

    expect(findTypeScriptRoots(rootPath)).toEqual([path.join(rootPath, 'apps', 'web')]);
  });

  it('detects matching files by extension', () => {
    writeFile('src/index.ts', 'export const app = true;');
    writeFile('src/types.d.ts', 'export interface User {}');

    expect(containsFile(rootPath, '.ts')).toBe(true);
  });
});

function writeFile(relativePath: string, content: string): void {
  const filePath = path.join(rootPath, relativePath);
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, content);
}
