import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { analyzeTypeScriptBoundaries } from '../src/analysis/type-script-boundaries.js';

let rootPath: string;

beforeEach(() => {
  rootPath = fs.mkdtempSync(path.join(os.tmpdir(), 'codemeridian-ts-boundaries-'));
});

afterEach(() => {
  fs.rmSync(rootPath, { recursive: true, force: true });
});

describe('analyzeTypeScriptBoundaries', () => {
  it('detects tsconfig-based package roots and eslint config presence', () => {
    fs.mkdirSync(path.join(rootPath, 'apps', 'web'), { recursive: true });
    fs.writeFileSync(path.join(rootPath, 'apps', 'web', 'tsconfig.json'), '{}');
    fs.writeFileSync(path.join(rootPath, 'apps', 'web', '.eslintrc.json'), '{}');
    fs.writeFileSync(path.join(rootPath, 'apps', 'web', 'index.ts'), 'export const x = 1;\n');

    const boundaries = analyzeTypeScriptBoundaries(rootPath);

    expect(boundaries).toHaveLength(1);
    expect(boundaries[0]).toMatchObject({
      rootPath: path.join(rootPath, 'apps', 'web'),
      hasEslintConfig: true,
    });
  });
});
