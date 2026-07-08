import { describe, expect, it } from 'vitest';
import { walkTypeScript } from '../src/walker.js';
import { useTempProject } from './walker-test-helpers.js';

const project = useTempProject();

describe('walkTypeScript test discovery', () => {
  it('indexes jest or vitest test callbacks as synthetic method nodes with call edges', () => {
    project.writeFile(
      'src/math.ts',
      `export function calculate() {
  return format();
}

export function format() {
  return 'ok';
}
`,
    );
    project.writeFile(
      'src/math.test.ts',
      `import { calculate } from './math';

it('calculates formatted output', () => {
  calculate();
});
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());
    const testCaseNode = result.nodes.find(node => node.name === '__testcase__.it.calculates formatted output@L3');

    expect(testCaseNode).toEqual(
      expect.objectContaining({
        type: 'Method',
        filePath: 'src/math.test.ts',
      }),
    );
    expect(result.edges).toContainEqual({
      sourceId: 'Proj:File:src_math.test.ts',
      targetId: testCaseNode!.id,
      type: 'Contains',
    });
    expect(result.edges).toContainEqual({
      sourceId: testCaseNode!.id,
      targetId: 'Proj:Method:src_math.ts:calculate',
      type: 'Calls',
    });
  });

  it('indexes chained test invocations such as test.each callbacks', () => {
    project.writeFile(
      'src/orders.ts',
      `export function submitOrder() {
  return true;
}
`,
    );
    project.writeFile(
      'src/orders.spec.ts',
      `import { submitOrder } from './orders';

test.each([[1]])('submits order %s', () => {
  submitOrder();
});
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());
    const testCaseNode = result.nodes.find(node => node.name === '__testcase__.test.submits order %s@L3');

    expect(testCaseNode).toEqual(
      expect.objectContaining({
        type: 'Method',
        filePath: 'src/orders.spec.ts',
      }),
    );
    expect(result.edges).toContainEqual({
      sourceId: testCaseNode!.id,
      targetId: 'Proj:Method:src_orders.ts:submitOrder',
      type: 'Calls',
    });
  });

  it('indexes it.only callbacks as direct test shield sources', () => {
    project.writeFile(
      'src/orders.ts',
      `export function replaceOrder() {
  return true;
}
`,
    );
    project.writeFile(
      'src/orders.test.ts',
      `import { replaceOrder } from './orders';

it.only('replaces an order', () => {
  replaceOrder();
});
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());
    const testCaseNode = result.nodes.find(node => node.name === '__testcase__.it.replaces an order@L3');

    expect(testCaseNode).toEqual(
      expect.objectContaining({
        namespace: 'test/src',
        filePath: 'src/orders.test.ts',
      }),
    );
    expect(result.edges).toContainEqual({
      sourceId: testCaseNode!.id,
      targetId: 'Proj:Method:src_orders.ts:replaceOrder',
      type: 'Calls',
    });
  });

  it('indexes test callbacks that call through interface-typed workflows', () => {
    project.writeFile(
      'src/contracts.ts',
      `export interface OrderWorkflow {
  run(): string;
}
`,
    );
    project.writeFile(
      'src/orders.ts',
      `import type { OrderWorkflow } from './contracts';

export function submitOrder(workflow: OrderWorkflow) {
  return workflow.run();
}
`,
    );
    project.writeFile(
      'src/orders.spec.ts',
      `import { submitOrder } from './orders';

test('submits through the workflow contract', () => {
  submitOrder({ run: () => 'ok' });
});
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());
    const testCaseNode = result.nodes.find(node => node.name === '__testcase__.test.submits through the workflow contract@L3');

    expect(testCaseNode).toEqual(
      expect.objectContaining({
        type: 'Method',
        filePath: 'src/orders.spec.ts',
      }),
    );
    expect(result.edges).toContainEqual({
      sourceId: testCaseNode!.id,
      targetId: 'Proj:Method:src_orders.ts:submitOrder',
      type: 'Calls',
    });
  });

  it('indexes test.skip callbacks so skipped test files still contribute discovery metadata', () => {
    project.writeFile(
      'src/orders.ts',
      `export function archiveOrder() {
  return true;
}
`,
    );
    project.writeFile(
      'src/orders.test.ts',
      `import { archiveOrder } from './orders';

test.skip('archives an order', () => {
  archiveOrder();
});
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());
    const testCaseNode = result.nodes.find(node => node.name === '__testcase__.test.archives an order@L3');

    expect(testCaseNode).toEqual(
      expect.objectContaining({
        namespace: 'test/src',
      }),
    );
    expect(result.edges).toContainEqual({
      sourceId: testCaseNode!.id,
      targetId: 'Proj:Method:src_orders.ts:archiveOrder',
      type: 'Calls',
    });
  });

  it('treats top-level tests folders as test namespaces and indexes callback edges', () => {
    project.writeFile(
      'src/orders.ts',
      `export function submitOrder() {
  return true;
}
`,
    );
    project.writeFile(
      'tests/orders-ui.test.ts',
      `import { submitOrder } from '../src/orders';

it('submits from the test folder', () => {
  submitOrder();
});
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());
    const testFileNode = result.nodes.find(node => node.id === 'Proj:File:tests_orders-ui.test.ts');
    const testCaseNode = result.nodes.find(node => node.name === '__testcase__.it.submits from the test folder@L3');

    expect(testFileNode).toEqual(
      expect.objectContaining({
        namespace: 'test/tests',
      }),
    );
    expect(testCaseNode).toEqual(
      expect.objectContaining({
        filePath: 'tests/orders-ui.test.ts',
        namespace: 'test/tests',
      }),
    );
    expect(result.edges).toContainEqual({
      sourceId: testCaseNode!.id,
      targetId: 'Proj:Method:src_orders.ts:submitOrder',
      type: 'Calls',
    });
  });

  it('treats __tests__ tsx files as test namespaces and indexes nested callbacks', () => {
    project.writeFile(
      'src/components/orders.ts',
      `export function submitOrder() {
  return true;
}
`,
    );
    project.writeFile(
      'src/components/__tests__/orders.spec.tsx',
      `import { describe, it } from 'vitest';
import { submitOrder } from '../orders';

describe('orders', () => {
  it('submits from the ui', () => {
    submitOrder();
  });
});
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());
    const testFileNode = result.nodes.find(node => node.id === 'Proj:File:src_components___tests___orders.spec.tsx');
    const testCaseNode = result.nodes.find(node => node.name === '__testcase__.it.submits from the ui@L5');

    expect(testFileNode).toEqual(
      expect.objectContaining({
        namespace: 'test/src/components/__tests__',
      }),
    );
    expect(testCaseNode).toEqual(
      expect.objectContaining({
        filePath: 'src/components/__tests__/orders.spec.tsx',
        namespace: 'test/src/components/__tests__',
      }),
    );
    expect(result.edges).toContainEqual({
      sourceId: testCaseNode!.id,
      targetId: 'Proj:Method:src_components_orders.ts:submitOrder',
      type: 'Calls',
    });
  });
});

