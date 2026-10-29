import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import type { WorkerIdentity } from './worker-identity.js';

export interface ProducerFileOutcome {
  path: string;
  sourceFingerprint: string | null;
  status: 'complete' | 'partial' | 'failed';
  diagnostics: string[];
  dialect?: string;
  databaseScope?: string;
}

/** Facts from the executing worker. Process exit is deliberately not a coverage claim. */
export class ProducerReport {
  readonly contractVersion = '1';
  readonly requestId = process.env.CODEMERIDIAN_PRODUCER_REQUEST_ID ?? 'standalone';
  readonly scope = process.env.CODEMERIDIAN_PRODUCER_SCOPE ?? '.';
  readonly files: ProducerFileOutcome[] = [];
  readonly capabilities: Record<string, 'complete' | 'partial' | 'unknown' | 'unsupported'> = {};
  readonly limitations: string[] = [];
  published = false;
  private readonly contents = new Map<string, string>();

  constructor(readonly analyzer: string, readonly project: string, readonly rootPath: string,
    readonly fingerprint: WorkerIdentity['fingerprint']) {}

  capture(files: readonly string[]): void {
    if (files.length > 10_000) throw new Error('Producer source inventory exceeds its budget.');
    let totalBytes = 0;
    const seen = new Set<string>();
    for (const file of files) {
      const absolute = path.resolve(file);
      const relative = path.relative(this.rootPath, absolute).replace(/\\/g, '/');
      if (!relative || relative === '..' || relative.startsWith('../') || path.isAbsolute(relative) || seen.has(relative))
        throw new Error('Producer sources must be unique files beneath the repository root.');
      seen.add(relative);
      const outcome: ProducerFileOutcome = { path: relative, sourceFingerprint: null, status: 'failed', diagnostics: [] };
      this.files.push(outcome);
      try {
        for (let current = absolute; ; current = path.dirname(current)) {
          if (fs.lstatSync(current).isSymbolicLink()) throw new Error('Linked source boundary.');
          if (current === path.resolve(this.rootPath)) break;
          if (path.dirname(current) === current) throw new Error('Source escaped its root.');
        }
        const size = fs.statSync(absolute).size;
        if (size > 4 * 1024 * 1024 || totalBytes + size > 64 * 1024 * 1024) throw new Error('Source budget exceeded.');
        const bytes = fs.readFileSync(absolute);
        totalBytes += bytes.length;
        if (bytes.length > 4 * 1024 * 1024 || totalBytes > 64 * 1024 * 1024) throw new Error('Source budget exceeded.');
        outcome.sourceFingerprint = 'sha256:' + createHash('sha256').update(bytes).digest('hex');
        this.contents.set(absolute, new TextDecoder('utf-8', { fatal: true }).decode(bytes));
        outcome.status = 'complete';
      } catch {
        outcome.diagnostics.push('source_unavailable_or_unsupported_encoding');
      }
    }
  }

  read = (file: string): string => {
    const text = this.contents.get(path.resolve(file));
    if (text === undefined) throw new Error('Source was not captured successfully.');
    return text;
  };

  mark(file: string, status: ProducerFileOutcome['status'], reason: string): void {
    const relative = path.relative(this.rootPath, file).replace(/\\/g, '/');
    const outcome = this.files.find(item => item.path === relative);
    if (!outcome) throw new Error('An outcome must belong to the selected inventory.');
    outcome.status = status;
    if (outcome.diagnostics.length < 8) outcome.diagnostics.push(reason.slice(0, 512));
  }

  write(): void {
    const destination = process.env.CODEMERIDIAN_PRODUCER_REPORT;
    if (!destination) return;
    const payload = JSON.stringify({ contractVersion: this.contractVersion, requestId: this.requestId,
      analyzer: this.analyzer, project: this.project, scope: this.scope, fingerprint: this.fingerprint,
      generation: process.env.CODEMERIDIAN_PRODUCER_GENERATION ? Number(process.env.CODEMERIDIAN_PRODUCER_GENERATION) : undefined,
      sourceFingerprint: process.env.CODEMERIDIAN_PRODUCER_SOURCE,
      published: this.published, files: this.files, capabilities: this.capabilities,
      limitations: this.limitations.slice(0, 16).map(value => value.slice(0, 512)) });
    if (Buffer.byteLength(payload) > 4 * 1024 * 1024) throw new Error('Producer report exceeds its budget.');
    fs.writeFileSync(destination, payload, { flag: 'wx', encoding: 'utf8' });
  }
}
