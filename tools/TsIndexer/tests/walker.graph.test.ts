import { describe, expect, it } from 'vitest';
import { walkTypeScript } from '../src/walker.js';
import { useTempProject } from './walker-test-helpers.js';

const project = useTempProject();

describe('walkTypeScript graph indexing', () => {
  it('indexes classes, methods, line metadata, and contains edges', () => {
    project.writeFile(
      'editor.ts',
      `export class TextSlideVideoEditorState {
  snapshot() {
    return {};
  }
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj');

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
    project.writeFile(
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

    const result = walkTypeScript(project.getRootPath(), 'Proj');

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:editor.ts:TextSlideVideoEditorState.addCaption',
      targetId: 'Proj:Method:editor.ts:TextSlideVideoEditorState.snapshot',
      type: 'Calls',
    });
  });

  it('resolves top-level function calls in the same file', () => {
    project.writeFile(
      'math.ts',
      `export function calculate() {
  return format();
}

function format() {
  return 'ok';
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj');

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:math.ts:calculate',
      targetId: 'Proj:Method:math.ts:format',
      type: 'Calls',
    });
  });

  it('resolves imported function calls across files when the callee is unambiguous', () => {
    project.writeFile(
      'shared/format.ts',
      `export function formatAmount() {
  return '$10';
}
`,
    );
    project.writeFile(
      'consumer.ts',
      `import { formatAmount } from './shared/format';

export function renderTotal() {
  return formatAmount();
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj');

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:consumer.ts:renderTotal',
      targetId: 'Proj:Method:shared_format.ts:formatAmount',
      type: 'Calls',
    });
  });

  it('resolves aliased imported function calls to the exported target method', () => {
    project.writeFile(
      'shared/format.ts',
      `export function formatAmount() {
  return '$10';
}
`,
    );
    project.writeFile(
      'consumer.ts',
      `import { formatAmount as formatCurrency } from './shared/format';

export function renderTotal() {
  return formatCurrency();
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj');

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:consumer.ts:renderTotal',
      targetId: 'Proj:Method:shared_format.ts:formatAmount',
      type: 'Calls',
    });
  });

  it('emits implements and inherits edges for local types', () => {
    project.writeFile(
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

    const result = walkTypeScript(project.getRootPath(), 'Proj');

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
    project.writeFile('state.ts', 'export class State {}\n');
    project.writeFile('consumer.ts', "import { State } from './state';\nexport const state = new State();\n");

    const result = walkTypeScript(project.getRootPath(), 'Proj');

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:File:consumer.ts',
      targetId: 'Proj:File:state.ts',
      type: 'DependsOn',
    });
  });

  it('does not emit local dependency edges for external package imports', () => {
    project.writeFile(
      'consumer.ts',
      `import { describe } from 'vitest';

export function register() {
  return describe;
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj');

    expect(result.edges).not.toContainEqual(
      expect.objectContaining({
        sourceId: 'Proj:File:consumer.ts',
        type: 'DependsOn',
      }),
    );
  });

  it('emits uses edges for referenced local types and module nodes for folders', () => {
    project.writeFile('models/state.ts', 'export interface State { id: string }\n');
    project.writeFile(
      'services/consumer.ts',
      `import type { State } from '../models/state';

export function useState(state: State): string {
  return state.id;
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj');

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
    project.writeFile('models/state.ts', 'export interface State { id: string }\n');
    project.writeFile('models/index.ts', "export { State as AppState } from './state';\n");
    project.writeFile(
      'services/consumer.ts',
      `import { AppState } from '../models';

export const current: AppState = { id: '1' };
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj');

    expect(result.edges).toContainEqual(
      expect.objectContaining({
        sourceId: 'Proj:File:services_consumer.ts',
        targetId: 'Proj:Interface:models_state.ts:State',
        type: 'Uses',
      }),
    );
  });

  it('resolves default imported local classes as uses edges', () => {
    project.writeFile(
      'models/order.ts',
      `export default class Order {
  id = '1';
}
`,
    );
    project.writeFile(
      'services/consumer.ts',
      `import Order from '../models/order';

export function createOrder(order: Order): string {
  return order.id;
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj');

    expect(result.edges).toContainEqual(
      expect.objectContaining({
        sourceId: 'Proj:Method:services_consumer.ts:createOrder',
        targetId: 'Proj:Class:models_order.ts:Order',
        type: 'Uses',
      }),
    );
    expect(result.edges).toContainEqual(
      expect.objectContaining({
        sourceId: 'Proj:File:services_consumer.ts',
        targetId: 'Proj:Class:models_order.ts:Order',
        type: 'Uses',
      }),
    );
  });

  it('indexes local type aliases as interface-like nodes and emits uses edges for them', () => {
    project.writeFile('models/order-shape.ts', "export type OrderShape = { id: string; total: number };\n");
    project.writeFile(
      'services/consumer.ts',
      `import type { OrderShape } from '../models/order-shape';

export function readOrder(order: OrderShape): number {
  return order.total;
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj');

    expect(result.nodes).toContainEqual(
      expect.objectContaining({
        id: 'Proj:Interface:models_order-shape.ts:OrderShape',
        type: 'Interface',
      }),
    );
    expect(result.edges).toContainEqual(
      expect.objectContaining({
        sourceId: 'Proj:File:services_consumer.ts',
        targetId: 'Proj:Interface:models_order-shape.ts:OrderShape',
        type: 'Uses',
      }),
    );
  });

  it('skips ambiguous method calls instead of creating noisy edges', () => {
    project.writeFile(
      'one.ts',
      `export class One {
  run() {}
}
`,
    );
    project.writeFile(
      'two.ts',
      `export class Two {
  run() {}
}
`,
    );
    project.writeFile(
      'caller.ts',
      `export function caller(value: { run(): void }) {
  value.run();
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj');

    expect(result.edges).not.toContainEqual(
      expect.objectContaining({
        sourceId: 'Proj:Method:caller.ts:caller',
        type: 'Calls',
      }),
    );
  });
});
