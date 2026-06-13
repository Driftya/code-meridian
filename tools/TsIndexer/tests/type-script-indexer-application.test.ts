import fs from 'node:fs';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TypeScriptIndexerApplication } from '../src/application/type-script-indexer-application.js';
import { saveTsIndexerCache } from '../src/storage/indexer-cache.js';
import type { ResolvedIndexCommandOptions } from '../src/cli/options.js';
import { useTempProject } from './walker-test-helpers.js';

const project = useTempProject('codemeridian-ts-app-');

describe('TypeScriptIndexerApplication', () => {
  const originalFetch = globalThis.fetch;

  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it('forces a full walk when requested even if the cache is warm', async () => {
    project.writeFile('tsconfig.json', '{"compilerOptions":{"target":"ES2022","module":"ESNext"}}');
    project.writeFile(
      'src/config.ts',
      [
        'export function readConfig() {',
        '  return process.env.CodeMeridian_Auth_ApiKey ?? "";',
        '}',
      ].join('\n'),
    );

    const rootPath = project.getRootPath();
    const cacheDirectory = path.join(rootPath, '.meridian', 'cache');
    saveTsIndexerCache(cacheDirectory, 'CodeMeridian', [
      {
        path: 'src/config.ts',
        lastWriteUtcTicks: fs.statSync(path.join(rootPath, 'src/config.ts')).mtimeMs,
        length: fs.statSync(path.join(rootPath, 'src/config.ts')).size,
        contentHash: 'unchanged-cache-hit',
      },
    ]);

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

    const app = new TypeScriptIndexerApplication();
    const options: ResolvedIndexCommandOptions = {
      rootPath,
      projectName: 'CodeMeridian',
      serverUrl: 'http://127.0.0.1:5100',
      clear: false,
      forceFull: true,
      includeDocs: false,
      watch: false,
      storageMode: 'repo',
      cacheDirectory,
    };

    const exitCode = await app.run(options);

    expect(exitCode).toBe(0);

    const configEdgeBodies = requests
      .filter(request => request.path === '/api/v1/knowledge/nodes/edges' && request.body)
      .map(request => JSON.parse(request.body!))
      .filter(body => body.type === 'ReadsConfig');

    expect(configEdgeBodies).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          targetId: 'CodeMeridian::ConfigurationKey::CodeMeridian_Auth_ApiKey',
          properties: expect.objectContaining({
            rawKey: 'CodeMeridian_Auth_ApiKey',
          }),
        }),
      ]),
    );
  });
});
