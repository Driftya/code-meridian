import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { HtmlCssIndexerApplication } from '../src/application/html-css-indexer-application.js';
import type { ResolvedIndexCommandOptions } from '@codemeridian/indexer-shared';

describe('HtmlCssIndexerApplication', () => {
  const createdRoots: string[] = [];

  afterEach(() => {
    vi.restoreAllMocks();
    for (const root of createdRoots.splice(0, createdRoots.length)) {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });

  it('accepts the shared batch-file contract and returns success', async () => {
    const rootPath = fs.mkdtempSync(path.join(os.tmpdir(), 'codemeridian-html-css-app-'));
    createdRoots.push(rootPath);
    fs.mkdirSync(path.join(rootPath, 'src'), { recursive: true });
    fs.writeFileSync(path.join(rootPath, 'src', 'app.html'), '<div class="hero"></div>');

    const batchFilePath = path.join(rootPath, 'batch.json');
    fs.writeFileSync(batchFilePath, JSON.stringify([{ Path: 'src/app.html', FileRole: 'UiMarkup' }]));

    const logs: string[] = [];
    vi.spyOn(console, 'log').mockImplementation(message => {
      logs.push(String(message));
    });

    const app = new HtmlCssIndexerApplication();
    const options: ResolvedIndexCommandOptions = {
      rootPath,
      projectName: 'CodeMeridian',
      serverUrl: 'http://127.0.0.1:5100',
      batchFilePath,
    };

    const exitCode = await app.run(options);

    expect(exitCode).toBe(0);
    expect(logs.some(message => message.includes('Batch size: 1 file(s)'))).toBe(true);
    expect(logs.some(message => message.includes('not implemented yet'))).toBe(true);
  });
});
