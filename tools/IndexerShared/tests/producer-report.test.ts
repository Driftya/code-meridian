import { createHash } from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ProducerReport } from '../src/producer-report.js';

let root: string;
beforeEach(() => { root = fs.mkdtempSync(path.join(os.tmpdir(), 'producer-report-')); });
afterEach(() => { vi.unstubAllEnvs(); fs.rmSync(root, { recursive: true, force: true }); });
const identity = { implementation: 'sha256:' + 'a'.repeat(64), parser: 'sha256:' + 'b'.repeat(64) };
const report = () => new ProducerReport('sql', 'Fixture', root, identity);

describe('executing producer evidence', () => {
  it('binds the actual consumed buffer even after the source changes', () => {
    const file = path.join(root, 'unicode.sql');
    const bytes = Buffer.from('SELECT 1; -- å');
    fs.writeFileSync(file, bytes);
    const audit = report(); audit.capture([file]);
    fs.writeFileSync(file, 'SELECT 2;');
    expect(audit.read(file)).toBe(bytes.toString());
    expect(audit.files[0].sourceFingerprint).toBe('sha256:' + createHash('sha256').update(bytes).digest('hex'));
  });

  it.each(['missing.sql', 'invalid.sql'])('cannot consume unavailable or invalid UTF-8: %s', name => {
    const file = path.join(root, name);
    if (name === 'invalid.sql') fs.writeFileSync(file, Buffer.from([0xff, 0xff]));
    const audit = report(); audit.capture([file]);
    expect(audit.files[0].status).toBe('failed');
    expect(() => audit.read(file)).toThrow();
  });

  it('refuses duplicate selections and escaped paths', () => {
    const file = path.join(root, 'a.sql'); fs.writeFileSync(file, 'SELECT 1');
    expect(() => report().capture([file, file])).toThrow(/unique/);
    expect(() => report().capture([path.join(root, '..', 'escaped.sql')])).toThrow(/beneath/);
  });

  it('writes a nonce-bound receipt once without inventing publication or coverage', () => {
    const destination = path.join(root, 'report.json');
    vi.stubEnv('CODEMERIDIAN_PRODUCER_REPORT', destination);
    vi.stubEnv('CODEMERIDIAN_PRODUCER_REQUEST_ID', 'nonce');
    const audit = report(); audit.write();
    expect(JSON.parse(fs.readFileSync(destination, 'utf8'))).toMatchObject({ requestId: 'nonce', published: false, capabilities: {} });
    expect(() => audit.write()).toThrow();
  });
});
