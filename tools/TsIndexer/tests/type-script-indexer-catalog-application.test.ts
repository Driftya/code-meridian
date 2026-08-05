import fs from 'node:fs';
import path from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { TypeScriptIndexerApplication } from '../src/application/type-script-indexer-application.js';
import { useTempProject } from './walker-test-helpers.js';

const project = useTempProject('codemeridian-ts-catalog-app-');

describe('TypeScriptIndexerApplication catalog evidence', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.restoreAllMocks();
  });

  it('persists a bounded partial-catalog reason when tsconfig loading fails', async () => {
    project.writeFile('tsconfig.json', '{ invalid json');
    project.writeFile('src/service.ts', 'export function run() { return 1; }\n');

    const rootPath = project.getRootPath();
    const batchFilePath = path.join(rootPath, 'batch.json');
    fs.writeFileSync(batchFilePath, JSON.stringify([{ path: 'src/service.ts' }]));

    const requests: Array<{ path: string; body?: string }> = [];
    globalThis.fetch = vi.fn(async (input, init) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
      requests.push({ path: new URL(url).pathname, body: init?.body?.toString() });
      return new Response('{}', { status: 201, headers: { 'Content-Type': 'application/json' } });
    }) as typeof fetch;

    await new TypeScriptIndexerApplication().run({
      rootPath,
      projectName: 'CodeMeridian',
      serverUrl: 'http://127.0.0.1:5100',
      batchFilePath,
      isIncremental: true,
    });

    const indexRun = readBodies(requests, '/api/v1/knowledge/nodes')
      .find(body => body.properties?.externalKind === 'IndexRun');
    expect(indexRun?.properties).toEqual(expect.objectContaining({
      language: 'TypeScript',
      mode: 'incremental',
      usedFullResolutionCatalog: 'false',
      resolutionCatalogCompleteness: 'partial',
      resolutionCatalogReason: 'tsconfig_load_failed',
      resolutionCatalogFileCount: '1',
      resolutionCatalogLoadDurationMs: expect.any(String),
      resolutionCatalogHeapUsedBytes: expect.any(String),
    }));
  });
});

function readBodies(
  requests: Array<{ path: string; body?: string }>,
  requestPath: string,
): Array<Record<string, any>> {
  return requests
    .filter(request => request.path === requestPath && request.body)
    .flatMap(request => {
      const parsed = JSON.parse(request.body!) as Record<string, any> | Array<Record<string, any>>;
      return Array.isArray(parsed) ? parsed : [parsed];
    });
}
