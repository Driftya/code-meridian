import { describe, expect, it } from 'vitest';
import { walkTypeScript } from '../src/walker.js';
import { useTempProject } from './walker-test-helpers.js';

const project = useTempProject('codemeridian-ts-catalog-');

describe('walkTypeScript resolution catalog', () => {
  it('matches full-run edges while emitting only changed-file nodes', () => {
    project.writeFile('tsconfig.json', '{"compilerOptions":{"target":"ES2022","module":"ESNext"}}');
    project.writeFile('target.ts', 'export function target() { return 1; }\n');
    project.writeFile(
      'caller.ts',
      "import { target } from './target';\nexport function caller() { return target(); }\n",
    );

    const callerPath = project.listTypeScriptFiles()
      .find(file => file.endsWith('caller.ts'))!;
    const result = walkTypeScript(
      project.getRootPath(),
      'Proj',
      [callerPath],
      undefined,
      undefined,
      true,
    );
    const fullResult = walkTypeScript(
      project.getRootPath(),
      'Proj',
      project.listTypeScriptFiles(),
    );
    const incrementalReplay = walkTypeScript(
      project.getRootPath(),
      'Proj',
      [callerPath],
      undefined,
      undefined,
      true,
    );
    const fullReplay = walkTypeScript(
      project.getRootPath(),
      'Proj',
      project.listTypeScriptFiles(),
    );

    expect(result.usedFullResolutionCatalog).toBe(true);
    expect(result.resolutionCatalog).toEqual(expect.objectContaining({
      completeness: 'full',
      sourceFileCount: 2,
    }));
    expect(result.nodes).toContainEqual(expect.objectContaining({
      id: 'Proj:Method:caller.ts:caller',
    }));
    expect(result.nodes).not.toContainEqual(expect.objectContaining({
      id: 'Proj:Method:target.ts:target',
    }));
    expect(result.edges.filter(edge => edge.type === 'Calls')).toEqual(
      fullResult.edges.filter(edge => edge.type === 'Calls'
        && edge.sourceId === 'Proj:Method:caller.ts:caller'),
    );
    expect(incrementalReplay.edges).toEqual(result.edges);
    expect(incrementalReplay.relationshipHealth).toEqual(result.relationshipHealth);
    expect(fullReplay.edges).toEqual(fullResult.edges);
    expect(fullReplay.relationshipHealth).toEqual(fullResult.relationshipHealth);
  });

  it('marks an invalid tsconfig fallback as a partial resolution catalog', () => {
    project.writeFile('tsconfig.json', '{ invalid json');
    project.writeFile('service.ts', 'export function run() { return 1; }\n');

    const result = walkTypeScript(
      project.getRootPath(),
      'Proj',
      project.listTypeScriptFiles(),
      undefined,
      undefined,
      true,
    );

    expect(result.usedFullResolutionCatalog).toBe(false);
    expect(result.resolutionCatalog).toEqual(expect.objectContaining({
      completeness: 'partial',
      reason: 'tsconfig_load_failed',
    }));
    expect(result.nodes).toContainEqual(expect.objectContaining({
      id: 'Proj:Method:service.ts:run',
    }));
  });
});
