import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { parseCommandLine } from '../src/cli/program.js';

let rootPath: string;
let globalConfigRoot: string;

beforeEach(() => {
  rootPath = fs.mkdtempSync(path.join(os.tmpdir(), 'codemeridian-ts-program-'));
  globalConfigRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'codemeridian-ts-global-'));
  process.env.CODEMERIDIAN_CONFIG_HOME = globalConfigRoot;
  process.env.CodeMeridian_Project = ' ';
  process.env.CodeMeridian_Url = ' ';
});

afterEach(() => {
  delete process.env.CODEMERIDIAN_CONFIG_HOME;
  delete process.env.CodeMeridian_Project;
  delete process.env.CodeMeridian_Url;
  fs.rmSync(rootPath, { recursive: true, force: true });
  fs.rmSync(globalConfigRoot, { recursive: true, force: true });
});

describe('parseCommandLine', () => {
  it('loads server url and global storage mode from global meridian config', async () => {
    fs.writeFileSync(
      path.join(globalConfigRoot, 'meridian.json'),
      JSON.stringify(
        {
          codeMeridianUrl: 'http://global:5100',
          useGlobalCache: true,
        },
        null,
        2,
      ),
    );

    const result = await parseCommandLine(['node', 'codemeridian-ts-indexer', rootPath]);

    expect(result.serverUrl).toBe('http://global:5100');
    expect(result.storageMode).toBe('global');
    expect(result.cacheDirectory).toContain(globalConfigRoot);
  });
});
