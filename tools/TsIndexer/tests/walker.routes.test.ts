import { describe, expect, it } from 'vitest';
import { walkTypeScript } from '../src/walker.js';
import { useTempProject } from './walker-test-helpers.js';

const project = useTempProject();

describe('walkTypeScript route indexing', () => {
  it('indexes fetch, axios, and request-style HTTP routes as ApiEndpoint links', () => {
    project.writeFile(
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

    const result = walkTypeScript(project.getRootPath(), 'Proj');

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

  it('normalizes absolute urls, route params, query strings, fragments, and trailing slashes', () => {
    project.writeFile(
      'api.ts',
      `export async function loadOrder() {
  return fetch('https://example.test/api/orders/:id/?draft=true#summary');
}

export async function updateOrder() {
  return client.patch('/api/orders/{orderId}/');
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj');

    expect(result.nodes).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          id: 'Proj::ApiEndpoint::GET /api/orders/{param}',
        }),
        expect.objectContaining({
          id: 'Proj::ApiEndpoint::PATCH /api/orders/{param}',
        }),
      ]),
    );
  });

  it('uses the enclosing method as the route source when a route call appears inside a class method', () => {
    project.writeFile(
      'api.ts',
      `export class OrdersClient {
  loadOrders() {
    return fetch('/api/orders');
  }
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj');

    expect(result.edges).toContainEqual(
      expect.objectContaining({
        sourceId: 'Proj:Method:api.ts:OrdersClient.loadOrders',
        targetId: 'Proj::ApiEndpoint::GET /api/orders',
        type: 'Calls',
      }),
    );
  });

  it('falls back to the file node when a route call is not inside a named function or method', () => {
    project.writeFile(
      'bootstrap.ts',
      `const warmup = fetch('/api/bootstrap');
export { warmup };
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj');

    expect(result.edges).toContainEqual(
      expect.objectContaining({
        sourceId: 'Proj:File:bootstrap.ts',
        targetId: 'Proj::ApiEndpoint::GET /api/bootstrap',
        type: 'Calls',
      }),
    );
  });

  it('does not emit ApiEndpoint nodes for unresolved dynamic route expressions', () => {
    project.writeFile(
      'api.ts',
      `const pathSegment = window.location.pathname;

export async function loadOrder() {
  return fetch(pathSegment);
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj');

    expect(result.nodes.filter(node => node.type === 'ApiEndpoint')).toHaveLength(0);
    expect(result.edges.filter(edge => edge.targetId.includes('::ApiEndpoint::'))).toHaveLength(0);
  });

  it('defaults fetch calls without an init object to GET endpoints', () => {
    project.writeFile(
      'api.ts',
      `export async function loadOrders() {
  return fetch('/api/orders');
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj');

    expect(result.nodes).toContainEqual(
      expect.objectContaining({
        id: 'Proj::ApiEndpoint::GET /api/orders',
        type: 'ApiEndpoint',
      }),
    );
    expect(result.edges).toContainEqual(
      expect.objectContaining({
        sourceId: 'Proj:Method:api.ts:loadOrders',
        targetId: 'Proj::ApiEndpoint::GET /api/orders',
        type: 'Calls',
      }),
    );
  });

  it('deduplicates repeated route calls to the same normalized endpoint from one source line', () => {
    project.writeFile(
      'api.ts',
      `const route = '/api/orders';

export function loadOrders() {
  fetch(route);
  fetch(route);
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj');
    const routeEdges = result.edges.filter(edge =>
      edge.sourceId === 'Proj:Method:api.ts:loadOrders'
      && edge.targetId === 'Proj::ApiEndpoint::GET /api/orders'
      && edge.type === 'Calls');

    expect(routeEdges).toHaveLength(2);
    expect(result.nodes.filter(node => node.id === 'Proj::ApiEndpoint::GET /api/orders')).toHaveLength(1);
  });

  it('marks awaited http client calls as async route edges', () => {
    project.writeFile(
      'api.ts',
      `export async function loadOrders() {
  return await axios.get('/api/orders');
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj');

    expect(result.edges).toContainEqual(
      expect.objectContaining({
        sourceId: 'Proj:Method:api.ts:loadOrders',
        targetId: 'Proj::ApiEndpoint::GET /api/orders',
        type: 'Calls',
        isAsync: true,
      }),
    );
  });
});
