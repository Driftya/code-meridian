import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ProducerReport } from '../../IndexerShared/dist/index.js';
import { SqlIndexerApplication } from '../src/application.js';
import { PostgreSqlDialectAnalyzer } from '../src/dialects/postgresql/analyzer.js';
import { SqlDialectRegistry } from '../src/dialect-registry.js';

let root: string;
beforeEach(() => { root = fs.mkdtempSync(path.join(os.tmpdir(), 'sql-producer-')); });
afterEach(() => { vi.unstubAllEnvs(); fs.rmSync(root, { recursive: true, force: true }); });
const fingerprint = { implementation: 'sha256:' + 'a'.repeat(64), parser: 'sha256:' + 'b'.repeat(64) };

describe('audited SQL producer', () => {
  it.each([
    ['SELECT 1;', 0, 'complete'],
    ['CREATE TABLE app.t(id integer);', 0, 'complete'],
    ['SELECT app.custom_function();', 2, 'partial'],
    ['SELECT * FROM unqualified;', 2, 'partial'],
    ['SELECT FROM !', 1, 'partial'],
  ])('reports explicit PostgreSQL/database scope evidence for %s', async (text, exit, coverage) => {
    const source = path.join(root, 'a.sql'); fs.writeFileSync(source, text);
    const batch = path.join(root, 'batch.json'); fs.writeFileSync(batch, JSON.stringify([{ path: 'a.sql' }]));
    const publish = vi.fn().mockResolvedValue(undefined);
    const app = new SqlIndexerApplication(new SqlDialectRegistry([new PostgreSqlDialectAnalyzer()]), publish);
    const report = new ProducerReport('sql', 'Fixture', root, fingerprint);
    vi.stubEnv('CODEMERIDIAN_PRODUCER_GENERATION', '3');
    vi.stubEnv('CODEMERIDIAN_PRODUCER_SOURCE', 'sha256:' + 'c'.repeat(64));
    const result = await app.run({ root, project: 'Fixture', serverUrl: 'http://fixture', batchFile: batch,
      settings: { enabled: true, defaultDialect: 'postgresql', databaseScope: 'orders' },
      cacheFile: path.join(root, 'cache'), workerFingerprint: fingerprint }, undefined, report);
    expect(result).toBe(exit);
    expect(report.published).toBe(true);
    expect(report.capabilities).toEqual({ sql_object_dependencies: coverage });
    expect(report.files[0]).toMatchObject({ path: 'a.sql', dialect: 'postgresql', databaseScope: 'orders' });
    expect(publish.mock.calls[0][0]).toMatchObject({ generation: 3, fingerprint,
      sourceFingerprint: 'sha256:' + 'c'.repeat(64), files: [expect.objectContaining({ sourceFingerprint: report.files[0].sourceFingerprint })] });
    expect(fs.existsSync(path.join(root, 'cache'))).toBe(exit === 0);
  });
});
