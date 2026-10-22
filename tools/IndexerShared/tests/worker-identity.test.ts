import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createWorkerIdentity, describeWorker, verifyWorkerIdentity } from '../src/worker-identity.js';

let root: string;
let entry: string;
let parser: string;
beforeEach(() => {
  root = fs.mkdtempSync(path.join(os.tmpdir(), 'meridian-identity-'));
  fs.mkdirSync(path.join(root, 'src'));
  parser = path.join(root, 'node_modules', 'fixture-parser');
  fs.mkdirSync(parser, { recursive: true });
  fs.writeFileSync(path.join(root, 'package.json'), '{"name":"worker","version":"1.0.0","dependencies":{"fixture-parser":"^1.0.0"}}');
  fs.writeFileSync(path.join(root, 'tsconfig.json'), '{}');
  fs.writeFileSync(path.join(root, 'src', 'index.ts'), 'export const worker = 1;');
  fs.writeFileSync(path.join(parser, 'package.json'), '{"name":"fixture-parser","version":"1.2.3","main":"index.js"}');
  fs.writeFileSync(path.join(parser, 'index.js'), 'throw new Error("Package identity must not execute package scripts");');
  entry = pathToFileURL(path.join(root, 'src', 'index.ts')).href;
});
afterEach(() => {
  vi.unstubAllEnvs(); vi.restoreAllMocks();
  fs.rmSync(root, { recursive: true, force: true });
});
function identity() { return createWorkerIdentity('fixture', entry, ['fixture-parser'], { grammar: 'actual' }); }

describe('executing worker identity', () => {
  it('uses the installed parser identity rather than the manifest range', () => {
    const report = identity();
    expect(report.parsers[0]).toMatchObject({ name: 'fixture-parser', version: '1.2.3' });
    expect(report.runtime.versions.node).toBe(process.versions.node);
    expect(report.fingerprint.implementation).toMatch(/^sha256:[a-f0-9]{64}$/);
    expect(identity()).toEqual(report);
  });

  it('invalidates parser identity for installed version and same-version runtime byte changes', () => {
    const first = identity();
    fs.writeFileSync(path.join(parser, 'package.json'), '{"name":"fixture-parser","version":"2.0.0","main":"index.js"}');
    const second = identity();
    expect(second.fingerprint.parser).not.toBe(first.fingerprint.parser);
    expect(second.fingerprint.implementation).toBe(first.fingerprint.implementation);
    fs.writeFileSync(path.join(parser, 'index.js'), 'module.exports = {};');
    expect(identity().fingerprint.parser).not.toBe(second.fingerprint.parser);
  });

  it('invalidates implementation identity when worker bytes change', () => {
    const first = identity();
    fs.writeFileSync(path.join(root, 'src', 'index.ts'), 'export const worker = 2;');
    expect(identity().fingerprint.implementation).not.toBe(first.fingerprint.implementation);
    expect(identity().fingerprint.parser).toBe(first.fingerprint.parser);
  });

  it('invalidates runtime identity when the executing Node version changes', () => {
    const first = identity();
    vi.spyOn(process, 'versions', 'get').mockReturnValue({ ...process.versions, node: 'different-node-runtime' });
    const second = identity();
    expect(second.fingerprint.implementation).not.toBe(first.fingerprint.implementation);
    expect(second.fingerprint.parser).not.toBe(first.fingerprint.parser);
  });

  it('does not manufacture an identity for missing parser assets or metadata', () => {
    fs.rmSync(path.join(parser, 'package.json'));
    expect(identity).toThrow('Missing runtime identity');
  });

  it('rejects identity changes while allowing explicitly unknown evidence', () => {
    const first = identity();
    vi.stubEnv('CODEMERIDIAN_EXPECTED_WORKER_FINGERPRINT', JSON.stringify(first.fingerprint));
    expect(() => verifyWorkerIdentity(first)).not.toThrow();
    fs.writeFileSync(path.join(root, 'src', 'index.ts'), 'export const changed = true;');
    expect(() => verifyWorkerIdentity(identity())).toThrow('Worker identity changed');
    vi.stubEnv('CODEMERIDIAN_EXPECTED_WORKER_FINGERPRINT', 'unknown');
    expect(() => verifyWorkerIdentity(identity())).not.toThrow();
    expect(() => verifyWorkerIdentity(identity(), JSON.stringify(first.fingerprint))).toThrow('Worker identity changed');
  });

  it('reports JSON only for the dedicated invocation', () => {
    const argv = vi.spyOn(process, 'argv', 'get').mockReturnValue(['node', 'worker', '--describe-worker']);
    const write = vi.spyOn(process.stdout, 'write').mockReturnValue(true);
    const report = identity();
    expect(describeWorker(report)).toBe(true);
    expect(write).toHaveBeenCalledWith(JSON.stringify(report) + '\n');
    argv.mockReturnValue(['node', 'worker', 'repository', '--describe-worker']);
    expect(describeWorker(report)).toBe(false);
  });
});
