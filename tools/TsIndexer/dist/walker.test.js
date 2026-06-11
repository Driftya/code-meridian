import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { walkTypeScript } from './walker.js';
describe('walkTypeScript route extraction', () => {
    const roots = [];
    afterEach(() => {
        for (const root of roots.splice(0)) {
            fs.rmSync(root, { recursive: true, force: true });
        }
    });
    it('indexes fetch and wrapper-style HTTP routes as ApiEndpoint links', () => {
        const root = fs.mkdtempSync(path.join(os.tmpdir(), 'codemeridian-ts-routes-'));
        roots.push(root);
        fs.writeFileSync(path.join(root, 'tsconfig.json'), JSON.stringify({ compilerOptions: { target: 'ES2022' } }));
        fs.writeFileSync(path.join(root, 'api.ts'), `
const routes = { orders: '/api/orders' };

export async function loadOrders() {
  return fetch(routes.orders, { method: 'POST' });
}

export async function loadOrder(id: string) {
  return client.get(\`/api/orders/\${id}\`);
}
`);
        const result = walkTypeScript(root, 'CodeMeridian');
        const endpointIds = result.nodes
            .filter(node => node.type === 'ApiEndpoint')
            .map(node => node.id);
        const routeEdges = result.edges.filter(edge => edge.targetId.includes('::ApiEndpoint::'));
        expect(endpointIds).toContain('CodeMeridian::ApiEndpoint::POST /api/orders');
        expect(endpointIds).toContain('CodeMeridian::ApiEndpoint::GET /api/orders/{param}');
        expect(routeEdges).toEqual(expect.arrayContaining([
            expect.objectContaining({
                type: 'Calls',
                targetId: 'CodeMeridian::ApiEndpoint::POST /api/orders',
                confidence: 0.95,
            }),
            expect.objectContaining({
                type: 'Calls',
                targetId: 'CodeMeridian::ApiEndpoint::GET /api/orders/{param}',
                confidence: 0.9,
            }),
        ]));
    });
});
