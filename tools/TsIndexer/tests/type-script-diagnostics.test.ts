import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { __testing, parseLintDiagnostics, parseTypeScriptDiagnostics } from '../src/diagnostics/type-script-diagnostics.js';

describe('parseTypeScriptDiagnostics', () => {
  it('parses tsc file diagnostics into stable diagnostic findings', () => {
    const output = [
      'src/app.ts(3,15): error TS2345: Argument of type \'string\' is not assignable to parameter of type \'number\'.',
      'src/util.ts(1,1): warning TS6133: \'unused\' is declared but its value is never read.',
    ].join('\n');

    const findings = parseTypeScriptDiagnostics(output, path.join('C:', 'repo'), path.join('C:', 'repo'), 'Project');

    expect(findings).toHaveLength(2);
    expect(findings[0]).toMatchObject({
      severity: 'error',
      code: 'TS2345',
      message: expect.stringContaining('string'),
      filePath: 'src/app.ts',
      line: 3,
      column: 15,
      source: 'tsc',
    });
    expect(findings[1]).toMatchObject({
      severity: 'warning',
      code: 'TS6133',
      filePath: 'src/util.ts',
    });
    expect(findings[0].id).not.toBe(findings[1].id);
  });

  it('parses eslint diagnostics grouped by file', () => {
    const output = [
      '/repo/src/app.ts',
      '  12:5  error  Unexpected any. Specify a different type  @typescript-eslint/no-explicit-any',
    ].join('\n');

    const findings = parseLintDiagnostics(output, '/repo', '/repo', 'Project');

    expect(findings).toHaveLength(1);
    expect(findings[0]).toMatchObject({
      severity: 'error',
      code: '@typescript-eslint/no-explicit-any',
      message: 'Unexpected any. Specify a different type',
      filePath: 'src/app.ts',
      line: 12,
      column: 5,
      source: 'eslint',
    });
  });

  it('builds diagnostic file source ids that match walker file nodes', () => {
    expect(__testing.buildDiagnosticFileSourceId('CodeMeridian', 'tools/TsIndexer/src/walker/graph.ts'))
      .toBe('CodeMeridian:File:tools_TsIndexer_src_walker_graph.ts');
  });
});
