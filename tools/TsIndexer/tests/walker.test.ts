import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { walkTypeScript } from '../src/walker.js';

let rootPath: string;

beforeEach(() => {
  rootPath = fs.mkdtempSync(path.join(os.tmpdir(), 'codemeridian-ts-indexer-'));
});

afterEach(() => {
  fs.rmSync(rootPath, { recursive: true, force: true });
});

describe('walkTypeScript', () => {
  it('indexes classes, methods, line metadata, and contains edges', () => {
    writeFile(
      'editor.ts',
      `export class TextSlideVideoEditorState {
  snapshot() {
    return {};
  }
}
`,
    );

    const result = walkTypeScript(rootPath, 'Proj');

    expect(result.nodes).toContainEqual(
      expect.objectContaining({
        id: 'Proj:Class:editor.ts:TextSlideVideoEditorState',
        type: 'Class',
        lineNumber: 1,
        lineCount: 5,
      }),
    );
    expect(result.nodes).toContainEqual(
      expect.objectContaining({
        id: 'Proj:Method:editor.ts:TextSlideVideoEditorState.snapshot',
        type: 'Method',
        lineNumber: 2,
        lineCount: 3,
      }),
    );
    expect(result.edges).toContainEqual({
      sourceId: 'Proj:File:editor.ts',
      targetId: 'Proj:Class:editor.ts:TextSlideVideoEditorState',
      type: 'Contains',
    });
    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Class:editor.ts:TextSlideVideoEditorState',
      targetId: 'Proj:Method:editor.ts:TextSlideVideoEditorState.snapshot',
      type: 'Contains',
    });
  });

  it('resolves same-class method calls', () => {
    writeFile(
      'editor.ts',
      `export class TextSlideVideoEditorState {
  snapshot() {
    return {};
  }

  addCaption() {
    this.snapshot();
  }
}
`,
    );

    const result = walkTypeScript(rootPath, 'Proj');

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:editor.ts:TextSlideVideoEditorState.addCaption',
      targetId: 'Proj:Method:editor.ts:TextSlideVideoEditorState.snapshot',
      type: 'Calls',
    });
  });

  it('resolves top-level function calls in the same file', () => {
    writeFile(
      'math.ts',
      `export function calculate() {
  return format();
}

function format() {
  return 'ok';
}
`,
    );

    const result = walkTypeScript(rootPath, 'Proj');

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:math.ts:calculate',
      targetId: 'Proj:Method:math.ts:format',
      type: 'Calls',
    });
  });

  it('emits implements and inherits edges for local types', () => {
    writeFile(
      'types.ts',
      `export interface BasePort {
}

export interface EditorPort extends BasePort {
}

export class BaseEditor {
}

export class Editor extends BaseEditor implements EditorPort {
}
`,
    );

    const result = walkTypeScript(rootPath, 'Proj');

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Interface:types.ts:EditorPort',
      targetId: 'Proj:Interface:types.ts:BasePort',
      type: 'Inherits',
    });
    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Class:types.ts:Editor',
      targetId: 'Proj:Class:types.ts:BaseEditor',
      type: 'Inherits',
    });
    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Class:types.ts:Editor',
      targetId: 'Proj:Interface:types.ts:EditorPort',
      type: 'Implements',
    });
  });

  it('emits depends-on edges for local imports', () => {
    writeFile('state.ts', 'export class State {}\n');
    writeFile('consumer.ts', "import { State } from './state';\nexport const state = new State();\n");

    const result = walkTypeScript(rootPath, 'Proj');

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:File:consumer.ts',
      targetId: 'Proj:File:state.ts',
      type: 'DependsOn',
    });
  });

  it('emits uses edges for referenced local types and module nodes for folders', () => {
    writeFile('models/state.ts', 'export interface State { id: string }\n');
    writeFile(
      'services/consumer.ts',
      `import type { State } from '../models/state';

export function useState(state: State): string {
  return state.id;
}
`,
    );

    const result = walkTypeScript(rootPath, 'Proj');

    expect(result.nodes).toContainEqual(
      expect.objectContaining({
        id: 'Proj:Module:models',
        type: 'Module',
      }),
    );
    expect(result.edges).toContainEqual(
      expect.objectContaining({
        sourceId: 'Proj:File:services_consumer.ts',
        targetId: 'Proj:Interface:models_state.ts:State',
        type: 'Uses',
      }),
    );
  });

  it('resolves aliased imports and re-export barrels to the underlying type', () => {
    writeFile('models/state.ts', 'export interface State { id: string }\n');
    writeFile('models/index.ts', "export { State as AppState } from './state';\n");
    writeFile(
      'services/consumer.ts',
      `import { AppState } from '../models';

export const current: AppState = { id: '1' };
`,
    );

    const result = walkTypeScript(rootPath, 'Proj');

    expect(result.edges).toContainEqual(
      expect.objectContaining({
        sourceId: 'Proj:File:services_consumer.ts',
        targetId: 'Proj:Interface:models_state.ts:State',
        type: 'Uses',
      }),
    );
  });

  it('skips ambiguous method calls instead of creating noisy edges', () => {
    writeFile(
      'one.ts',
      `export class One {
  run() {}
}
`,
    );
    writeFile(
      'two.ts',
      `export class Two {
  run() {}
}
`,
    );
    writeFile(
      'caller.ts',
      `export function caller(value: { run(): void }) {
  value.run();
}
`,
    );

    const result = walkTypeScript(rootPath, 'Proj');

    expect(result.edges).not.toContainEqual(
      expect.objectContaining({
        sourceId: 'Proj:Method:caller.ts:caller',
        type: 'Calls',
      }),
    );
  });

  it('indexes fetch, axios, and request-style HTTP routes as ApiEndpoint links', () => {
    writeFile(
      'api.ts',
      `const routes = { orders: '/api/orders' };
const orderRoute = '/api/orders';

export async function loadOrders() {
  return fetch(routes.orders, { method: 'POST' });
}

export async function createOrder() {
  return await axios.post(orderRoute);
}

export async function loadOrder(id: string) {
  return client.get(\`/api/orders/\${id}\`);
}

export function deleteOrder(id: string) {
  return client.request({
    method: 'DELETE',
    url: \`/api/orders/\${id}\`
  });
}
`,
    );

    const result = walkTypeScript(rootPath, 'Proj');

    expect(result.nodes).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          id: 'Proj::ApiEndpoint::POST /api/orders',
          type: 'ApiEndpoint',
        }),
        expect.objectContaining({
          id: 'Proj::ApiEndpoint::GET /api/orders/{param}',
          type: 'ApiEndpoint',
        }),
        expect.objectContaining({
          id: 'Proj::ApiEndpoint::DELETE /api/orders/{param}',
          type: 'ApiEndpoint',
        }),
      ]),
    );

    const routeEdges = result.edges.filter(edge => edge.targetId.includes('::ApiEndpoint::'));
    expect(routeEdges).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          type: 'Calls',
          targetId: 'Proj::ApiEndpoint::POST /api/orders',
          confidence: 0.95,
        }),
        expect.objectContaining({
          type: 'Calls',
          targetId: 'Proj::ApiEndpoint::GET /api/orders/{param}',
          confidence: 0.9,
        }),
        expect.objectContaining({
          type: 'Calls',
          targetId: 'Proj::ApiEndpoint::DELETE /api/orders/{param}',
          confidence: 0.9,
        }),
      ]),
    );
  });
});

function writeFile(relativePath: string, content: string): void {
  const filePath = path.join(rootPath, relativePath);
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, content);
}
