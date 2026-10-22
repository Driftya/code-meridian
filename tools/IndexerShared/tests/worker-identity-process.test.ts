import path from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { spawnSync } from 'node:child_process';
import { describe, expect, it } from 'vitest';
import type { WorkerIdentity } from '../src/worker-identity.js';

describe('worker entrypoint identity protocol', () => {
  it.each([['typescript', 'TsIndexer'], ['frontend', 'HtmlCssIndexer'], ['sql', 'SqlIndexer']])(
    '%s reports the executing parser and refuses a changed expected identity before indexing', (analyzer, worker) => {
      const entry = fileURLToPath(new URL(`../../${worker}/src/index.ts`, import.meta.url));
      const require = createRequire(entry);
      const argumentsPrefix = ['--import', pathToFileURL(require.resolve('tsx/esm')).href, entry];
      const options = { cwd: path.dirname(entry), encoding: 'utf8' as const, timeout: 20000, maxBuffer: 8192 };
      const described = spawnSync(process.execPath, [...argumentsPrefix, '--describe-worker'], options);
      expect(described.error).toBeUndefined();
      expect(described.status, described.stderr).toBe(0);
      const report = JSON.parse(described.stdout) as WorkerIdentity;
      expect(report).toMatchObject({ contractVersion: '1', analyzer });
      expect(report.fingerprint.implementation).toMatch(/^sha256:[a-f0-9]{64}$/);
      expect(report.fingerprint.parser).toMatch(/^sha256:[a-f0-9]{64}$/);
      if (analyzer !== 'sql') expect(report.parserRuntime.typescript).toBe(require('ts-morph').ts.version);
      else expect(report.parserRuntime.postgresqlGrammar).toBeGreaterThan(0);
      const compiled = spawnSync(process.execPath,
        [path.resolve(path.dirname(entry), '..', 'dist', 'index.js'), '--describe-worker'], options);
      expect(compiled.status, compiled.stderr).toBe(0);
      const compiledReport = JSON.parse(compiled.stdout) as WorkerIdentity;
      expect(compiledReport).toMatchObject({ contractVersion: '1', analyzer, parserRuntime: report.parserRuntime });
      expect(compiledReport.parsers.map(parser => parser.name)).not.toContain('tsx');
      const failed = spawnSync(process.execPath, [...argumentsPrefix, 'missing-repository'], {
        ...options, env: { ...process.env, CODEMERIDIAN_EXPECTED_WORKER_FINGERPRINT: JSON.stringify({
          ...report.fingerprint, parser: 'sha256:' + '0'.repeat(64),
        }) },
      });
      expect(failed.status).toBe(1);
      expect(failed.stderr).toContain('Worker identity changed');
    }, 30000);
});
