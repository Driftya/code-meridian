import fs from 'node:fs';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TypeScriptIndexerApplication } from '../src/application/type-script-indexer-application.js';
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

  it('indexes the orchestrated batch and preserves configured file roles', async () => {
    project.writeFile('tsconfig.json', '{"compilerOptions":{"target":"ES2022","module":"ESNext"}}');
    project.writeFile(
      'src/config/AppConfig.ts',
      [
        'export function readConfig() {',
        '  return process.env.CodeMeridian_Auth_ApiKey ?? "";',
        '}',
      ].join('\n'),
    );

    const rootPath = project.getRootPath();
    const batchFilePath = path.join(rootPath, 'batch.json');
    fs.writeFileSync(
      batchFilePath,
      JSON.stringify([
        {
          path: 'src/config/AppConfig.ts',
          fileRole: 'Configuration',
        },
      ]),
    );

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
      batchFilePath,
    };

    const exitCode = await app.run(options);

    expect(exitCode).toBe(0);

    const appConfigNode = readBodies(requests, '/api/v1/knowledge/nodes/bulk')
      .find(body => body.filePath === 'src/config/AppConfig.ts' && body.type === 'File');

    expect(appConfigNode?.fileRole).toBe('Configuration');

    const configEdgeBodies = readBodies(requests, '/api/v1/knowledge/nodes/edges/bulk')
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

  it('indexes batch entries without a file role', async () => {
    project.writeFile('tsconfig.json', '{"compilerOptions":{"target":"ES2022","module":"ESNext"}}');
    project.writeFile('src/app/service.ts', 'export function readValue() { return 1; }\n');

    const rootPath = project.getRootPath();
    const batchFilePath = path.join(rootPath, 'batch.json');
    fs.writeFileSync(
      batchFilePath,
      JSON.stringify([
        {
          path: 'src/app/service.ts',
        },
      ]),
    );

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
      batchFilePath,
    };

    const exitCode = await app.run(options);

    expect(exitCode).toBe(0);

    const serviceFileNode = readBodies(requests, '/api/v1/knowledge/nodes/bulk')
      .find(body => body.filePath === 'src/app/service.ts' && body.type === 'File');

    expect(serviceFileNode).toBeDefined();
    expect(serviceFileNode.fileRole).toBeUndefined();
  });

  it('normalizes Windows-style batch paths when mapping file roles', async () => {
    project.writeFile('tsconfig.json', '{"compilerOptions":{"target":"ES2022","module":"ESNext"}}');
    project.writeFile('src/config/AppConfig.ts', 'export const value = process.env.API_KEY ?? "";\n');

    const rootPath = project.getRootPath();
    const batchFilePath = path.join(rootPath, 'batch.json');
    fs.writeFileSync(
      batchFilePath,
      JSON.stringify([
        {
          path: 'src\\config\\AppConfig.ts',
          fileRole: 'Configuration',
        },
      ]),
    );

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
      batchFilePath,
    };

    const exitCode = await app.run(options);

    expect(exitCode).toBe(0);

    const fileNode = readBodies(requests, '/api/v1/knowledge/nodes/bulk')
      .find(body => body.filePath === 'src/config/AppConfig.ts' && body.type === 'File');

    expect(fileNode?.fileRole).toBe('Configuration');
  });

  it('accepts PascalCase batch keys written by the .NET indexer', async () => {
    project.writeFile('tsconfig.json', '{"compilerOptions":{"target":"ES2022","module":"ESNext"}}');
    project.writeFile('src/config/AppConfig.ts', 'export const value = process.env.API_KEY ?? "";\n');

    const rootPath = project.getRootPath();
    const batchFilePath = path.join(rootPath, 'batch.json');
    fs.writeFileSync(
      batchFilePath,
      JSON.stringify([
        {
          Path: 'src/config/AppConfig.ts',
          FileRole: 'Configuration',
        },
      ]),
    );

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
      batchFilePath,
    };

    const exitCode = await app.run(options);

    expect(exitCode).toBe(0);

    const fileNode = readBodies(requests, '/api/v1/knowledge/nodes/bulk')
      .find(body => body.filePath === 'src/config/AppConfig.ts' && body.type === 'File');

    expect(fileNode?.fileRole).toBe('Configuration');
  });

  it('uploads node batches before edge batches when using bulk ingest', async () => {
    project.writeFile('tsconfig.json', '{"compilerOptions":{"target":"ES2022","module":"ESNext"}}');
    project.writeFile(
      'src/config/AppConfig.ts',
      [
        'export const ApiConfig = {',
        '  apiKey: process.env.CodeMeridian_Auth_ApiKey ?? "",',
        '};',
        'export function readConfig() {',
        '  return ApiConfig.apiKey;',
        '}',
      ].join('\n'),
    );

    const rootPath = project.getRootPath();
    const batchFilePath = path.join(rootPath, 'batch.json');
    fs.writeFileSync(
      batchFilePath,
      JSON.stringify([
        {
          path: 'src/config/AppConfig.ts',
          fileRole: 'Configuration',
        },
      ]),
    );

    let activeNodeRequests = 0;
    let edgeStartedBeforeNodesFinished = false;

    globalThis.fetch = vi.fn(async (input) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
      const requestPath = new URL(url).pathname;

      if (requestPath === '/api/v1/knowledge/nodes/bulk') {
        activeNodeRequests++;
        await new Promise(resolve => setTimeout(resolve, 10));
        activeNodeRequests--;
      } else if (requestPath === '/api/v1/knowledge/nodes/edges/bulk' && activeNodeRequests > 0) {
        edgeStartedBeforeNodesFinished = true;
      }

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
      batchFilePath,
    };

    const exitCode = await app.run(options);

    expect(exitCode).toBe(0);
    expect(activeNodeRequests).toBe(0);
    expect(edgeStartedBeforeNodesFinished).toBe(false);
  });

  it('fails fast for malformed batch files', async () => {
    project.writeFile('tsconfig.json', '{"compilerOptions":{"target":"ES2022","module":"ESNext"}}');

    const rootPath = project.getRootPath();
    const batchFilePath = path.join(rootPath, 'batch.json');
    fs.writeFileSync(batchFilePath, '{not-json');

    const app = new TypeScriptIndexerApplication();
    const options: ResolvedIndexCommandOptions = {
      rootPath,
      projectName: 'CodeMeridian',
      serverUrl: 'http://127.0.0.1:5100',
      batchFilePath,
    };

    await expect(app.run(options)).rejects.toThrow();
  });

  it('continues when a bulk node ingest request fails and reports node errors', async () => {
    project.writeFile('tsconfig.json', '{"compilerOptions":{"target":"ES2022","module":"ESNext"}}');
    project.writeFile('src/config/AppConfig.ts', 'export const value = process.env.API_KEY ?? "";\n');

    const rootPath = project.getRootPath();
    const batchFilePath = path.join(rootPath, 'batch.json');
    fs.writeFileSync(batchFilePath, JSON.stringify([{ path: 'src/config/AppConfig.ts', fileRole: 'Configuration' }]));

    const warnings: string[] = [];
    vi.spyOn(console, 'warn').mockImplementation(message => {
      warnings.push(String(message));
    });

    globalThis.fetch = vi.fn(async (input) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
      const requestPath = new URL(url).pathname;

      if (requestPath === '/api/v1/knowledge/nodes/bulk') {
        return new Response('boom', { status: 500 });
      }

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
      batchFilePath,
    };

    const exitCode = await app.run(options);

    expect(exitCode).toBe(0);
    expect(warnings.some(message => message.includes('warn: node'))).toBe(true);
  });
});

function readBodies(requests: Array<{ path: string; body?: string }>, expectedPath: string): any[] {
  return requests
    .filter(request => request.path === expectedPath && request.body)
    .flatMap(request => {
      const parsed = JSON.parse(request.body!);
      return Array.isArray(parsed) ? parsed : [parsed];
    });
}
