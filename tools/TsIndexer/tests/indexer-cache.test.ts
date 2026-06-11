import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import {
  buildTsIndexerPlan,
  getCacheFilePath,
  loadTsIndexerCache,
  saveTsIndexerCache,
} from '../src/storage/indexer-cache.js';

let rootPath: string;
let cachePath: string;

beforeEach(() => {
  rootPath = fs.mkdtempSync(path.join(os.tmpdir(), 'codemeridian-ts-cache-'));
  cachePath = fs.mkdtempSync(path.join(os.tmpdir(), 'codemeridian-ts-cache-store-'));
});

afterEach(() => {
  fs.rmSync(rootPath, { recursive: true, force: true });
  fs.rmSync(cachePath, { recursive: true, force: true });
});

describe('ts indexer cache', () => {
  it('persists and reloads the file snapshot', () => {
    const file = writeFile('src/app.ts', 'export const app = true;\n');
    const plan = buildTsIndexerPlan(rootPath, [file], undefined);

    saveTsIndexerCache(cachePath, 'Project', plan.nextState);

    const reloaded = loadTsIndexerCache(cachePath, 'Project');
    expect(reloaded).toBeDefined();
    expect(reloaded?.files).toHaveLength(1);
    expect(getCacheFilePath(cachePath, 'Project')).toContain('ts-indexer-files-');
  });

  it('detects changed and deleted files from the previous snapshot', () => {
    const fileA = writeFile('src/a.ts', 'export const a = 1;\n');
    const fileB = writeFile('src/b.ts', 'export const b = 2;\n');
    const initial = buildTsIndexerPlan(rootPath, [fileA, fileB], undefined);
    saveTsIndexerCache(cachePath, 'Project', initial.nextState);

    fs.writeFileSync(fileA, 'export const a = 3;\n');
    fs.rmSync(fileB);

    const next = buildTsIndexerPlan(rootPath, [fileA], loadTsIndexerCache(cachePath, 'Project'));
    expect(next.changedFiles).toEqual(['src/a.ts']);
    expect(next.deletedFiles).toEqual(['src/b.ts']);
  });

  function writeFile(relativePath: string, content: string): string {
    const filePath = path.join(rootPath, relativePath);
    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    fs.writeFileSync(filePath, content);
    return filePath;
  }
});
