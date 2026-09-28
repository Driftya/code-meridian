import { describe, expect, it } from 'vitest';
import { walkTypeScript } from '../src/walker.js';
import { useTempProject } from './walker-test-helpers.js';

const project = useTempProject();
const walk = () => walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

describe('callable signature provenance', () => {
  it('classifies destructured library callbacks by their external signature', () => {
    project.writeFile('node_modules/hooks/index.d.ts', `
      type Setter<T> = (value: T) => void;
      export function useState<T>(value: T): [T, Setter<T>];
    `);
    project.writeFile('component.ts', `
      import { useState } from 'hooks';
      export function Component() {
        const [value, setValue] = useState(false);
        setValue(true);
      }
    `);
    const result = walk();
    expect(result.relationshipHealth.calls.unresolvedLocal).toBe(0);
    expect(result.relationshipHealth.calls.externalOrUnindexed).toBe(2);
    expect(result.edges.filter(edge => edge.type === 'Calls')).toEqual([]);
  });

  it('keeps local callback fields indeterminate when only a signature is known', () => {
    project.writeFile('controller.ts', `
      export class Controller {
        private unsubscribe: (() => void) | null = null;
        stop() { this.unsubscribe?.(); }
      }
    `);
    const result = walk();
    expect(result.relationshipHealth.calls.unresolvedLocal).toBe(0);
    expect(result.relationshipHealth.calls.reasons['indeterminate:callable_signature']).toBe(1);
    expect(result.edges.filter(edge => edge.type === 'Calls')).toEqual([]);
  });

  it('still reports a local implementation that the catalog cannot represent', () => {
    project.writeFile('controller.ts', `
      export class Controller {
        execute = () => {};
        run() { this.execute(); }
      }
    `);
    expect(walk().relationshipHealth.calls.unresolvedLocal).toBe(1);
  });
});
