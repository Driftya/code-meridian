import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { parseCommandLine } from '../src/cli/program.js';

let rootPath: string;
let batchFilePath: string;

beforeEach(() => {
  rootPath = fs.mkdtempSync(path.join(os.tmpdir(), 'codemeridian-ts-program-'));
  batchFilePath = path.join(rootPath, 'batch.json');
  fs.writeFileSync(batchFilePath, '[]');
  process.env.CodeMeridian_Auth_ApiKey = 'test-api-key';
});

afterEach(() => {
  delete process.env.CodeMeridian_Auth_ApiKey;
  fs.rmSync(rootPath, { recursive: true, force: true });
});

describe('parseCommandLine', () => {
  it('resolves the internal worker contract explicitly', async () => {
    const result = await parseCommandLine([
      'node',
      'codemeridian-ts-indexer',
      rootPath,
      '--project',
      'CodeMeridian',
      '--url',
      'http://localhost:5100',
      '--batch-file',
      batchFilePath,
    ]);

    expect(result.rootPath).toBe(path.resolve(rootPath));
    expect(result.projectName).toBe('CodeMeridian');
    expect(result.serverUrl).toBe('http://localhost:5100');
    expect(result.batchFilePath).toBe(path.resolve(batchFilePath));
    expect(result.apiKey).toBe('test-api-key');
  });

  it('normalizes a blank api key to undefined', async () => {
    process.env.CodeMeridian_Auth_ApiKey = ' ';

    const result = await parseCommandLine([
      'node',
      'codemeridian-ts-indexer',
      rootPath,
      '--project',
      'CodeMeridian',
      '--url',
      'http://localhost:5100',
      '--batch-file',
      batchFilePath,
    ]);

    expect(result.apiKey).toBeUndefined();
  });
});
