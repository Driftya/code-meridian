import { describe, expect, it } from 'vitest';
import { walkTypeScript } from '../src/walker.js';
import { useTempProject } from './walker-test-helpers.js';

const project = useTempProject();

describe('walkTypeScript graph indexing', () => {
  it('indexes JavaScript and JSX files through the TypeScript graph walker', () => {
    project.writeFile(
      'jsconfig.json',
      '{"compilerOptions":{"baseUrl":".","paths":{"@/*":["src/*"]},"jsx":"react-jsx"}}',
    );
    project.writeFile(
      'src/service.js',
      `export function loadOrders() { return []; }

export class OrderService {
  list() {
    return loadOrders();
  }
}
`,
    );
    project.writeFile(
      'src/Orders.jsx',
      `import { loadOrders } from '@/service.js';

export function Orders() {
  const handleClick = () => loadOrders();
  return <button onClick={handleClick}>Load</button>;
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.nodes).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: 'Proj:File:src_service.js', filePath: 'src/service.js', type: 'File' }),
      expect.objectContaining({ id: 'Proj:Method:src_service.js:loadOrders', type: 'Method' }),
      expect.objectContaining({ id: 'Proj:Class:src_service.js:OrderService', type: 'Class' }),
      expect.objectContaining({ id: 'Proj:File:src_Orders.jsx', filePath: 'src/Orders.jsx', type: 'File' }),
      expect.objectContaining({ id: 'Proj:Method:src_Orders.jsx:Orders', type: 'Method' }),
    ]));
    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:src_Orders.jsx:Orders',
      targetId: 'Proj:Method:src_service.js:loadOrders',
      type: 'Calls',
    });
  });

  it('classifies every call candidate exactly once and reports duplicate emitted edges', () => {
    project.writeFile(
      'health.ts',
      `const localHandler = createHandler();

function helper() {}

export function run() {
  helper();
  helper();
  localHandler();
  missingReceiver.execute();
  console.log('done');
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());
    const repeated = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());
    const calls = result.relationshipHealth.calls;

    expect(calls.attempted).toBe(
      calls.resolvedLocal + calls.externalOrUnindexed + calls.unresolvedLocal + calls.indeterminate,
    );
    expect(calls.resolvedLocal).toBeGreaterThanOrEqual(2);
    expect(calls.duplicateEdges).toBeGreaterThanOrEqual(1);
    expect(calls.unresolvedLocal).toBeGreaterThanOrEqual(1);
    expect(calls.indeterminate).toBeGreaterThanOrEqual(1);
    expect(calls.externalOrUnindexed).toBeGreaterThanOrEqual(1);
    expect(result.edges.filter(edge => edge.type === 'Calls'
      && edge.sourceId.endsWith(':run')
      && edge.targetId.endsWith(':helper'))).toHaveLength(1);
    expect(repeated.relationshipHealth).toEqual(result.relationshipHealth);
  });

  it('classifies declarations in excluded dependency and build paths as external', () => {
    project.writeFile(
      'tsconfig.json',
      '{"compilerOptions":{"module":"NodeNext","moduleResolution":"NodeNext","target":"ES2022"}}',
    );
    project.writeFile(
      'node_modules/example-package/package.json',
      '{"name":"example-package","type":"module","types":"index.d.ts"}',
    );
    project.writeFile(
      'node_modules/example-package/index.d.ts',
      `export interface ExternalClient {
  execute(): string;
}

export function createClient(): ExternalClient;
`,
    );
    project.writeFile(
      'dist/generated.d.ts',
      `export interface GeneratedOptions {
  enabled: boolean;
}

export function readGenerated(options: GeneratedOptions): boolean;
`,
    );
    project.writeFile(
      'consumer.ts',
      `import { createClient, type ExternalClient } from 'example-package';
import { readGenerated, type GeneratedOptions } from './dist/generated.js';

export function run(client: ExternalClient, options: GeneratedOptions) {
  createClient();
  client.execute();
  return readGenerated(options);
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.relationshipHealth.calls.reasons['external_or_unindexed:declaration_in_excluded_path'])
      .toBeGreaterThanOrEqual(2);
    expect(result.relationshipHealth.typeReferences.reasons['external_or_unindexed:declaration_in_excluded_path'])
      .toBeGreaterThanOrEqual(2);
    expect(result.relationshipHealth.calls.reasons)
      .not.toHaveProperty('unresolved_local:local_declaration_not_indexed');
    expect(result.relationshipHealth.typeReferences.reasons)
      .not.toHaveProperty('unresolved_local:local_declaration_not_indexed');
  });

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

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

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

  it('indexes constructors as method nodes and captures constructor calls', () => {
    project.writeFile(
      'editor.ts',
      `export class TextSlideVideoEditorState {
  constructor() {
    this.snapshot();
  }

  snapshot() {
    return {};
  }
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.nodes).toContainEqual(
      expect.objectContaining({
        id: 'Proj:Method:editor.ts:TextSlideVideoEditorState.constructor',
        type: 'Method',
      }),
    );
    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Class:editor.ts:TextSlideVideoEditorState',
      targetId: 'Proj:Method:editor.ts:TextSlideVideoEditorState.constructor',
      type: 'Contains',
    });
    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:editor.ts:TextSlideVideoEditorState.constructor',
      targetId: 'Proj:Method:editor.ts:TextSlideVideoEditorState.snapshot',
      type: 'Calls',
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

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:editor.ts:TextSlideVideoEditorState.addCaption',
      targetId: 'Proj:Method:editor.ts:TextSlideVideoEditorState.snapshot',
      type: 'Calls',
    });
  });

  it('uses semantic receiver types across factory-return chains', () => {
    project.writeFile(
      'semantic-chain.ts',
      `class Repository {
  save() {}
}

class RepositoryFactory {
  create(): Repository {
    return new Repository();
  }
}

export function persist() {
  const repository = new RepositoryFactory().create();
  repository.save();
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:semantic-chain.ts:persist',
      targetId: 'Proj:Method:semantic-chain.ts:Repository.save',
      type: 'Calls',
    });
  });

  it('does not use a unique same-name method when a member semantic target is not indexed', () => {
    project.writeFile(
      'semantic-negative.ts',
      `class LocalWorker {
  execute() {}
}

declare const externalWorker: { execute(): void };

export function run() {
  externalWorker.execute();
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.edges).not.toContainEqual({
      sourceId: 'Proj:Method:semantic-negative.ts:run',
      targetId: 'Proj:Method:semantic-negative.ts:LocalWorker.execute',
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

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:math.ts:calculate',
      targetId: 'Proj:Method:math.ts:format',
      type: 'Calls',
    });
  });

  it('indexes top-level arrow-function variables as method-like nodes and resolves calls to them', () => {
    project.writeFile(
      'math.ts',
      `export const format = () => 'ok';

export function calculate() {
  return format();
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.nodes).toContainEqual(
      expect.objectContaining({
        id: 'Proj:Method:math.ts:format',
        type: 'Method',
      }),
    );
    expect(result.edges).toContainEqual({
      sourceId: 'Proj:File:math.ts',
      targetId: 'Proj:Method:math.ts:format',
      type: 'Contains',
    });
    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:math.ts:calculate',
      targetId: 'Proj:Method:math.ts:format',
      type: 'Calls',
    });
  });

  it('gives same-named top-level functions stable distinct ids across files', () => {
    project.writeFile(
      'src/frontend/auth.ts',
      `export function isAuthorized() {
  return true;
}
`,
    );
    project.writeFile(
      'src/backend/auth.ts',
      `export function isAuthorized() {
  return true;
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());
    const authMethods = result.nodes.filter(node => node.type === 'Method' && node.name === 'isAuthorized');

    expect(authMethods).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          id: 'Proj:Method:src_frontend_auth.ts:isAuthorized',
        }),
        expect.objectContaining({
          id: 'Proj:Method:src_backend_auth.ts:isAuthorized',
        }),
      ]),
    );
    expect(new Set(authMethods.map(node => node.id)).size).toBe(authMethods.length);
  });

  it('captures source snippets for methods', () => {
    project.writeFile(
      'orders.ts',
      `export class OrderService {
  placeOrder() {
    this.validateOrder();
  }

  validateOrder() {
  }
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());
    const method = result.nodes.find(node => node.id === 'Proj:Method:orders.ts:OrderService.placeOrder');

    expect(method).toEqual(
      expect.objectContaining({
        sourceSnippet: expect.stringContaining('placeOrder()'),
      }),
    );
    expect(method?.sourceSnippet).toContain('this.validateOrder();');
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

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

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

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:consumer.ts:renderTotal',
      targetId: 'Proj:Method:shared_format.ts:formatAmount',
      type: 'Calls',
    });
  });

  it('resolves imported calls to exported arrow-function variables', () => {
    project.writeFile(
      'shared/format.ts',
      `export const formatAmount = () => '$10';
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

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:consumer.ts:renderTotal',
      targetId: 'Proj:Method:shared_format.ts:formatAmount',
      type: 'Calls',
    });
  });

  it('resolves function calls imported through re-export barrels', () => {
    project.writeFile(
      'shared/format.ts',
      `export function formatAmount() {
  return '$10';
}
`,
    );
    project.writeFile('shared/index.ts', "export { formatAmount } from './format';\n");
    project.writeFile(
      'consumer.ts',
      `import { formatAmount } from './shared';

export function renderTotal() {
  return formatAmount();
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:consumer.ts:renderTotal',
      targetId: 'Proj:Method:shared_format.ts:formatAmount',
      type: 'Calls',
    });
  });

  it('resolves calls to default-exported functions across files', () => {
    project.writeFile(
      'shared/format.ts',
      `export default function formatAmount() {
  return '$10';
}
`,
    );
    project.writeFile(
      'consumer.ts',
      `import formatAmount from './shared/format';

export function renderTotal() {
  return formatAmount();
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:consumer.ts:renderTotal',
      targetId: 'Proj:Method:shared_format.ts:formatAmount',
      type: 'Calls',
    });
  });

  it('resolves calls to default-exported functions through barrel re-exports', () => {
    project.writeFile(
      'shared/format.ts',
      `export default function formatAmount() {
  return '$10';
}
`,
    );
    project.writeFile('shared/index.ts', "export { default as formatAmount } from './format';\n");
    project.writeFile(
      'consumer.ts',
      `import { formatAmount } from './shared';

export function renderTotal() {
  return formatAmount();
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:consumer.ts:renderTotal',
      targetId: 'Proj:Method:shared_format.ts:formatAmount',
      type: 'Calls',
    });
  });

  it('resolves calls through namespace imports to exported functions', () => {
    project.writeFile(
      'shared/format.ts',
      `export function formatAmount() {
  return '$10';
}
`,
    );
    project.writeFile(
      'consumer.ts',
      `import * as money from './shared/format';

export function renderTotal() {
  return money.formatAmount();
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:consumer.ts:renderTotal',
      targetId: 'Proj:Method:shared_format.ts:formatAmount',
      type: 'Calls',
    });
  });

  it('resolves calls to imported class methods through constructed instances', () => {
    project.writeFile(
      'shared/editor.ts',
      `export class Editor {
  format() {
    return 'ok';
  }
}
`,
    );
    project.writeFile(
      'consumer.ts',
      `import { Editor } from './shared/editor';

export function renderTotal() {
  return new Editor().format();
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:consumer.ts:renderTotal',
      targetId: 'Proj:Method:shared_editor.ts:Editor.format',
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

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

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

  it('resolves interface-typed method calls to interface member nodes', () => {
    project.writeFile(
      'contracts.ts',
      `export interface OrderWorkflow {
  run(): string;
}
`,
    );
    project.writeFile(
      'workflow.ts',
      `import type { OrderWorkflow } from './contracts';

export class CheckoutWorkflow implements OrderWorkflow {
  run() {
    return 'ok';
  }
}
`,
    );
    project.writeFile(
      'consumer.ts',
      `import type { OrderWorkflow } from './contracts';
import { CheckoutWorkflow } from './workflow';

export function dispatch(workflow: OrderWorkflow = new CheckoutWorkflow()) {
  return workflow.run();
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Class:workflow.ts:CheckoutWorkflow',
      targetId: 'Proj:Interface:contracts.ts:OrderWorkflow',
      type: 'Implements',
    });
    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Interface:contracts.ts:OrderWorkflow',
      targetId: 'Proj:Method:contracts.ts:OrderWorkflow.run',
      type: 'Contains',
    });
    expect(result.edges).toContainEqual({
      sourceId: 'Proj:Method:consumer.ts:dispatch',
      targetId: 'Proj:Method:contracts.ts:OrderWorkflow.run',
      type: 'Calls',
    });
  });

  it('emits depends-on edges for local imports', () => {
    project.writeFile('state.ts', 'export class State {}\n');
    project.writeFile('consumer.ts', "import { State } from './state';\nexport const state = new State();\n");

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

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

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

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

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

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

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

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

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

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

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

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

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.edges).not.toContainEqual(
      expect.objectContaining({
        sourceId: 'Proj:Method:caller.ts:caller',
        type: 'Calls',
      }),
    );
  });
});

