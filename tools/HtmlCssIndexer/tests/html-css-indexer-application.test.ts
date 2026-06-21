import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { HtmlCssIndexerApplication } from '../src/application/html-css-indexer-application.js';
import type { ResolvedIndexCommandOptions } from '#indexer-shared';

describe('HtmlCssIndexerApplication', () => {
  const createdRoots: string[] = [];
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    vi.restoreAllMocks();
    globalThis.fetch = originalFetch;
    for (const root of createdRoots.splice(0, createdRoots.length)) {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });

  it('indexes frontend relationships from the shared batch-file contract', async () => {
    const rootPath = fs.mkdtempSync(path.join(os.tmpdir(), 'codemeridian-html-css-app-'));
    createdRoots.push(rootPath);
    fs.mkdirSync(path.join(rootPath, 'src'), { recursive: true });
    fs.writeFileSync(
      path.join(rootPath, 'src', 'app.html'),
      '<link rel="stylesheet" href="./app.scss"><div class="hero" id="page-root"></div>',
    );
    fs.writeFileSync(
      path.join(rootPath, 'src', 'Card.tsx'),
      'import "./app.scss"; export const Card = () => <div className={`hero ${"hero--wide"}`} />;',
    );
    fs.writeFileSync(
      path.join(rootPath, 'src', 'app.scss'),
      '.hero { color: var(--brand); }\n.hero--wide { margin: 0; }\n:root { --brand: #fff; }',
    );

    const batchFilePath = path.join(rootPath, 'batch.json');
    fs.writeFileSync(batchFilePath, JSON.stringify([
      { Path: 'src/app.html', FileRole: 'Unknown' },
      { Path: 'src/Card.tsx', FileRole: 'Unknown' },
      { Path: 'src/app.scss', FileRole: 'Unknown' },
    ]));

    const logs: string[] = [];
    vi.spyOn(console, 'log').mockImplementation(message => {
      logs.push(String(message));
    });
    const requests: Array<{ path: string; body?: string }> = [];
    globalThis.fetch = vi.fn(async (input, init) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
      requests.push({
        path: new URL(url).pathname,
        body: init?.body?.toString(),
      });

      return new Response('{}', {
        status: 201,
        headers: { 'Content-Type': 'application/json' },
      });
    }) as typeof fetch;

    const app = new HtmlCssIndexerApplication();
    const options: ResolvedIndexCommandOptions = {
      rootPath,
      projectName: 'CodeMeridian',
      serverUrl: 'http://127.0.0.1:5100',
      batchFilePath,
    };

    const exitCode = await app.run(options);

    expect(exitCode).toBe(0);
    expect(logs.some(message => message.includes('Batch size: 3 file(s)'))).toBe(true);
    expect(logs.some(message => message.includes('Building frontend graph...'))).toBe(true);
    expect(logs.some(message => message.includes('Processed 3/3 frontend files'))).toBe(true);
    expect(logs.some(message => message.includes('Uploading nodes...'))).toBe(true);
    expect(logs.some(message => message.includes('Uploaded'))).toBe(true);
    expect(requests.some(request => request.path === '/api/v1/knowledge/nodes')).toBe(true);
    expect(requests.some(request => request.path === '/api/v1/knowledge/nodes/edges')).toBe(true);

    const edgeBodies = requests
      .filter(request => request.path === '/api/v1/knowledge/nodes/edges' && request.body)
      .map(request => JSON.parse(request.body!));

    expect(edgeBodies).toEqual(expect.arrayContaining([
      expect.objectContaining({ type: 'UsesClass' }),
      expect.objectContaining({ type: 'DefinesSelector' }),
      expect.objectContaining({ type: 'ImportsStyle' }),
      expect.objectContaining({ type: 'Uses', properties: expect.objectContaining({ relationshipKind: 'DefinesStyleDeclaration' }) }),
    ]));
  });
});
