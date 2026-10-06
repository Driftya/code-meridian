import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SqlIndexerApplication } from '../src/application.js';
import { SqlDialectRegistry } from '../src/dialect-registry.js';
import { PostgreSqlDialectAnalyzer } from '../src/dialects/postgresql/analyzer.js';
import type { SqlRunOptions } from '../src/application.js';

const roots: string[] = [];
afterEach(() => { for (const root of roots.splice(0)) fs.rmSync(root, { recursive: true, force: true }); });
function fixture(): SqlRunOptions {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'meridian-sql-')); roots.push(root);
  fs.writeFileSync(path.join(root, 'a.sql'), 'CREATE TABLE app.t(id int);');
  fs.writeFileSync(path.join(root, 'b.sql'), 'SELECT * FROM app.t;');
  const batchFile = path.join(root, 'batch.json');
  fs.writeFileSync(batchFile, JSON.stringify([{ path: 'a.sql', fileRole: 'Migration' }, { path: 'b.sql' }]));
  return { root, project: 'SqlTest', serverUrl: 'http://fixture', batchFile,
    cacheFile: path.join(root, 'cache', 'fingerprint'), settings: { enabled: true, defaultDialect: 'postgresql', databaseScope: 'main' } };
}
function application(publish = vi.fn().mockResolvedValue(undefined)) {
  return { publish, app: new SqlIndexerApplication(new SqlDialectRegistry([new PostgreSqlDialectAnalyzer()]), publish) };
}

describe('SQL publication and cache', () => {
  it('skips unchanged runs and reanalyzes unchanged references after declaration changes', async () => {
    const options = fixture(); const { app, publish } = application();
    expect(await app.run(options)).toBe(0);
    expect(publish.mock.calls[0][0].files[0].nodes.find((n: { type: string }) => n.type === 'File').fileRole).toBe('Migration');
    expect(await app.run(options)).toBe(0); expect(publish).toHaveBeenCalledTimes(1);
    fs.writeFileSync(path.join(options.root, 'a.sql'), 'CREATE VIEW app.t AS SELECT 1;');
    await app.run(options);
    expect(publish.mock.calls[1][0].files[1].nodes.some((n: { type: string }) => n.type === 'DatabaseView')).toBe(true);
    await app.run({ ...options, settings: { ...options.settings, databaseScope: 'other' } });
    expect(publish).toHaveBeenCalledTimes(3);
  });

  it('does not advance cache on transport or parse failure and retries failed files', async () => {
    const options = fixture(); const { app, publish } = application();
    publish.mockRejectedValueOnce(new Error('transport failure'));
    await expect(app.run(options)).rejects.toThrow('transport');
    expect(fs.existsSync(options.cacheFile)).toBe(false);
    await app.run(options); const before = fs.readFileSync(options.cacheFile, 'utf8');
    fs.writeFileSync(path.join(options.root, 'a.sql'), 'SELECT FROM !');
    expect(await app.run(options)).toBe(1);
    expect(fs.readFileSync(options.cacheFile, 'utf8')).toBe(before);
    expect(publish.mock.calls.at(-1)![0].files.map((f: { status: string }) => f.status)).toEqual(['failed', 'complete']);
    expect(await app.run(options)).toBe(1);
  });

  it('publishes deletion inventories, caches partial coverage and rejects unknown dialects', async () => {
    const options = fixture(); const { app, publish } = application();
    fs.writeFileSync(path.join(options.root, 'b.sql'), 'SELECT * FROM unknown;');
    await app.run(options); await app.run(options); expect(publish).toHaveBeenCalledTimes(1);
    fs.writeFileSync(options.batchFile, '[]'); await app.run(options);
    expect(publish.mock.calls[1][0].files).toEqual([]);
    await expect(app.run({ ...options, settings: { ...options.settings, defaultDialect: 'mysql' } })).rejects.toThrow('Unsupported');
    expect(publish).toHaveBeenCalledTimes(2);
  });
});
