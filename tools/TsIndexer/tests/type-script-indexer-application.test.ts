import fs from 'node:fs';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TypeScriptIndexerApplication } from '../src/application/type-script-indexer-application.js';
import type { ResolvedIndexCommandOptions } from '../src/cli/options.js';
import { useTempProject } from './walker-test-helpers.js';

const project = useTempProject('codemeridian-ts-app-');

describe('TypeScriptIndexerApplication', () => {
  const originalFetch = globalThis.fetch;

  const setupMockFetch = () => {
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

    return requests;
  };

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

    const requests = setupMockFetch();

    const app = new TypeScriptIndexerApplication();
    const options: ResolvedIndexCommandOptions = {
      rootPath,
      projectName: 'CodeMeridian',
      serverUrl: 'http://127.0.0.1:5100',
      batchFilePath,
    };

    const exitCode = await app.run(options);

    expect(exitCode).toBe(0);

    const appConfigNode = requests
      .filter(request => request.path === '/api/v1/knowledge/nodes' && request.body)
      .map(request => JSON.parse(request.body!))
      .find(body => body.filePath === 'src/config/AppConfig.ts' && body.type === 'File');

    expect(appConfigNode?.fileRole).toBe('Configuration');

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

    const serviceFileNode = requests
      .filter(request => request.path === '/api/v1/knowledge/nodes' && request.body)
      .map(request => JSON.parse(request.body!))
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

    const fileNode = requests
      .filter(request => request.path === '/api/v1/knowledge/nodes' && request.body)
      .map(request => JSON.parse(request.body!))
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

    const fileNode = requests
      .filter(request => request.path === '/api/v1/knowledge/nodes' && request.body)
      .map(request => JSON.parse(request.body!))
      .find(body => body.filePath === 'src/config/AppConfig.ts' && body.type === 'File');

    expect(fileNode?.fileRole).toBe('Configuration');
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
});
