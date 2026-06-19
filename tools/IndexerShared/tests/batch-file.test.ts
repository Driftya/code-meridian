import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { readIndexerBatchFile } from '../src/batch-file.js';

let rootPath: string;

beforeEach(() => {
  rootPath = fs.mkdtempSync(path.join(os.tmpdir(), 'codemeridian-shared-batch-'));
});

afterEach(() => {
  fs.rmSync(rootPath, { recursive: true, force: true });
});

describe('readIndexerBatchFile', () => {
  it('normalizes Windows-style paths and PascalCase batch keys', () => {
    const batchFilePath = path.join(rootPath, 'batch.json');
    fs.writeFileSync(
      batchFilePath,
      JSON.stringify([
        {
          Path: 'src\\config\\AppConfig.ts',
          FileRole: 'Configuration',
        },
      ]),
    );

    const batch = readIndexerBatchFile(rootPath, batchFilePath);

    expect(batch.files).toEqual([path.join(rootPath, 'src', 'config', 'AppConfig.ts')]);
    expect(batch.fileRoles.get('src/config/AppConfig.ts')).toBe('Configuration');
  });

  it('fails fast when an entry is missing a path', () => {
    const batchFilePath = path.join(rootPath, 'batch.json');
    fs.writeFileSync(batchFilePath, JSON.stringify([{ fileRole: 'Configuration' }]));

    expect(() => readIndexerBatchFile(rootPath, batchFilePath)).toThrow(/without a path/i);
  });
});
