import { describe, expect, it } from 'vitest';
import { walkTypeScript } from '../src/walker.js';
import { useTempProject } from './walker-test-helpers.js';

const project = useTempProject();
const walk = () => walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

describe('relationship resolution regressions', () => {
  it('indexes scoped local functions without confusing equal names', () => {
    project.writeFile('nested.ts', `
      export function first() { function helper() {} helper(); }
      export function second() { function helper() {} helper(); }
    `);
    const result = walk();
    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:nested.ts:first', targetId: 'Proj:Method:nested.ts:first.helper', type: 'Calls',
    });
    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:nested.ts:second', targetId: 'Proj:Method:nested.ts:second.helper', type: 'Calls',
    });
    expect(result.relationshipHealth.calls.unresolvedLocal).toBe(0);
  });

  it('indexes nested arrow functions and keeps supplied callback properties indeterminate', () => {
    project.writeFile('nested.ts', `
      export function run(options: { onSuccess: () => void }) {
        const worker = () => options.onSuccess();
        worker();
      }
    `);
    const result = walk();
    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:nested.ts:run', targetId: 'Proj:Method:nested.ts:run.worker', type: 'Calls',
    });
    expect(result.relationshipHealth.calls.unresolvedLocal).toBe(0);
    expect(result.relationshipHealth.calls.reasons).toHaveProperty('indeterminate:callable_property');
  });

  it('attributes nested function bodies to their own callers only', () => {
    project.writeFile('nested.ts', `
      function target() {}
      export function run() { const worker = () => target(); }
    `);
    const calls = walk().edges.filter(edge => edge.type === 'Calls');
    expect(calls).toContainEqual({
      sourceId: 'Proj:Method:nested.ts:run.worker', targetId: 'Proj:Method:nested.ts:target', type: 'Calls',
    });
    expect(calls.some(edge => edge.sourceId === 'Proj:Method:nested.ts:run')).toBe(false);
  });

  it('keeps external callable properties classified outside the graph', () => {
    project.writeFile('node_modules/external/index.d.ts', 'export const client: { execute: () => void };');
    project.writeFile('consumer.ts', `import { client } from 'external'; export function run() { client.execute(); }`);
    const health = walk().relationshipHealth.calls;
    expect(health.externalOrUnindexed).toBe(1);
    expect(health.indeterminate).toBe(0);
  });

  it('does not treat type parameters or const assertions as missing graph targets', () => {
    project.writeFile('generic.ts', `
      export function identity<T>(value: T): T { return value; }
      export function options() { return { enabled: true } as const; }
    `);
    const result = walk();
    expect(result.relationshipHealth.typeReferences.unresolvedLocal).toBe(0);
    expect(result.relationshipHealth.typeReferences.indeterminate).toBe(0);
  });

  it('keeps callback targets indeterminate without inventing a same-name call', () => {
    project.writeFile('callback.ts', `
      export function run(resolve: () => void) { resolve(); }
    `);
    project.writeFile('unrelated.ts', 'export function resolve() {}');
    const result = walk();
    expect(result.edges.filter(edge => edge.type === 'Calls')).toEqual([]);
    expect(result.relationshipHealth.calls.unresolvedLocal).toBe(0);
    expect(result.relationshipHealth.calls.reasons['indeterminate:callable_parameter']).toBe(1);
  });

  it('does not bind external imports to unrelated local functions', () => {
    project.writeFile('node_modules/external/index.d.ts', 'export function execute(): void;');
    project.writeFile('consumer.ts', `import { execute } from 'external'; export function run() { execute(); }`);
    project.writeFile('unrelated.ts', 'export function execute() {}');
    const result = walk();
    expect(result.edges.filter(edge => edge.type === 'Calls')).toEqual([]);
    expect(result.relationshipHealth.calls.externalOrUnindexed).toBe(1);
  });

  it('resolves aliased interface heritage by declaration instead of first short-name match', () => {
    project.writeFile('a.ts', 'export interface Service { run(): void; }');
    project.writeFile('b.ts', 'export interface Service { run(): void; }');
    project.writeFile('consumer.ts', `
      import { Service as Contract } from './b';
      export class Consumer implements Contract { run() {} }
    `);
    expect(walk().edges).toContainEqual({
      sourceId: 'Proj:Class:consumer.ts:Consumer', targetId: 'Proj:Interface:b.ts:Service', type: 'Implements',
    });
  });

  it('resolves interface methods through element access', () => {
    project.writeFile('contract.ts', `
      interface Contract { run(): void; }
      export function execute(client: Contract) { client['run'](); }
    `);
    expect(walk().edges).toContainEqual({
      sourceId: 'Proj:Method:contract.ts:execute', targetId: 'Proj:Method:contract.ts:Contract.run', type: 'Calls',
    });
  });

  it('retains the member name after a generic call earlier in a chain', () => {
    project.writeFile('chain.ts', `
      declare function create<T>(): { finish(): void };
      export function run() { create<string>().finish(); }
    `);
    expect(walk().relationshipHealth.calls.reasons).not.toHaveProperty('indeterminate:missing_call_name');
  });
});
