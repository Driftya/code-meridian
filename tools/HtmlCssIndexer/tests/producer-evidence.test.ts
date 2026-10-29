import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ProducerReport } from '../../IndexerShared/dist/index.js';
import { HtmlCssIndexerApplication } from '../src/application/html-css-indexer-application.js';

let root: string;
beforeEach(() => { root = fs.mkdtempSync(path.join(os.tmpdir(), 'frontend-producer-')); });
afterEach(() => { vi.unstubAllGlobals(); fs.rmSync(root, { recursive: true, force: true }); });
const fingerprint = { implementation: 'sha256:' + 'a'.repeat(64), parser: 'sha256:' + 'b'.repeat(64) };

describe('frontend producer boundaries', () => {
  it.each([['.card { color: red; }', 0], ['.card {', 2]])('separates syntax outcome from relationships and cascade readiness', async (style, exit) => {
    fs.writeFileSync(path.join(root, 'style.css'), style);
    fs.writeFileSync(path.join(root, 'page.html'), "<div class='single-quoted'></div>");
    const batch = path.join(root, 'batch.json');
    fs.writeFileSync(batch, JSON.stringify([{ path: 'style.css' }, { path: 'page.html' }]));
    const requests: string[] = [];
    vi.stubGlobal('fetch', vi.fn(async (_: unknown, options: { body?: string }) => {
      requests.push(options.body ?? '');
      return new Response('{}', { status: 201, headers: { 'Content-Type': 'application/json' } });
    }));
    const report = new ProducerReport('frontend', 'Fixture', root, fingerprint);
    const result = await new HtmlCssIndexerApplication().run({ rootPath: root, projectName: 'Fixture', serverUrl: 'http://fixture', batchFilePath: batch }, report);
    expect(result).toBe(exit);
    expect(report.capabilities.frontend_cascade).toBe('unsupported');
    expect(report.capabilities.frontend_relationships).not.toBe('complete');
    expect(report.files.find(file => file.path === 'style.css')!.status).toBe(exit ? 'failed' : 'complete');
    if (exit) expect(requests.join('')).not.toContain('StyleSheetFile');
  });
});
