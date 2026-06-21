import { describe, expect, it, vi } from 'vitest';
import { CodeMeridianClient } from '../src/client.js';
import type { CodeEdgeDto, CodeNodeDto } from '../src/types.js';

describe('CodeMeridianClient', () => {
  it('ingests node batches through the bulk endpoint with bounded concurrency and reports success counts', async () => {
    const originalFetch = globalThis.fetch;
    const releases: Array<() => void> = [];
    let inFlight = 0;
    let maxInFlight = 0;
    const requestPaths: string[] = [];
    const requestBodies: CodeNodeDto[][] = [];

    globalThis.fetch = vi.fn(async (input, init) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
      requestPaths.push(new URL(url).pathname);
      requestBodies.push(JSON.parse(init?.body?.toString() ?? '[]') as CodeNodeDto[]);
      inFlight++;
      maxInFlight = Math.max(maxInFlight, inFlight);
      await new Promise<void>(resolve => {
        releases.push(resolve);
      });
      inFlight--;

      return new Response('{}', {
        status: 201,
        headers: { 'Content-Type': 'application/json' },
      });
    }) as typeof fetch;

    try {
      const client = new CodeMeridianClient('http://127.0.0.1:5100');
      const nodes: CodeNodeDto[] = [
        createNode('Node1'),
        createNode('Node2'),
        createNode('Node3'),
        createNode('Node4'),
        createNode('Node5'),
      ];

      const ingestPromise = client.ingestNodes(nodes, { concurrency: 2, batchSize: 2 });
      await vi.waitFor(() => {
        expect(releases.length).toBe(2);
      });

      releases.splice(0, releases.length).forEach(release => release());
      await vi.waitFor(() => {
        expect(releases.length).toBe(1);
      });

      releases.splice(0, releases.length).forEach(release => release());
      const result = await ingestPromise;

      expect(result).toEqual({
        successCount: 5,
        errorCount: 0,
      });
      expect(maxInFlight).toBe(2);
      expect(requestPaths).toEqual([
        '/api/v1/knowledge/nodes/bulk',
        '/api/v1/knowledge/nodes/bulk',
        '/api/v1/knowledge/nodes/bulk',
      ]);
      expect(requestBodies).toEqual([
        [createNode('Node1'), createNode('Node2')],
        [createNode('Node3'), createNode('Node4')],
        [createNode('Node5')],
      ]);
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it('continues after bulk ingest failures and reports both counts per failed item', async () => {
    const originalFetch = globalThis.fetch;
    const edgeErrors: string[] = [];
    let requestCount = 0;
    const requestPaths: string[] = [];

    globalThis.fetch = vi.fn(async (input) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
      requestPaths.push(new URL(url).pathname);
      requestCount++;
      if (requestCount === 2) {
        return new Response('boom', { status: 500 });
      }

      return new Response('{}', {
        status: 201,
        headers: { 'Content-Type': 'application/json' },
      });
    }) as typeof fetch;

    try {
      const client = new CodeMeridianClient('http://127.0.0.1:5100');
      const result = await client.ingestEdges(
        [createEdge('A', 'B'), createEdge('B', 'C'), createEdge('C', 'D'), createEdge('D', 'E')],
        {
          concurrency: 1,
          batchSize: 2,
          onError: (edge, error) => {
            edgeErrors.push(`${edge.sourceId}->${edge.targetId}:${String(error)}`);
          },
        },
      );

      expect(result).toEqual({
        successCount: 2,
        errorCount: 2,
      });
      expect(requestPaths).toEqual([
        '/api/v1/knowledge/nodes/edges/bulk',
        '/api/v1/knowledge/nodes/edges/bulk',
      ]);
      expect(edgeErrors).toHaveLength(2);
      expect(edgeErrors[0]).toContain('C->D');
      expect(edgeErrors[1]).toContain('D->E');
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it('reports all node items in a failed bulk batch', async () => {
    const originalFetch = globalThis.fetch;
    const nodeErrors: string[] = [];

    globalThis.fetch = vi.fn(async () =>
      new Response('boom', { status: 500 }),
    ) as typeof fetch;

    try {
      const client = new CodeMeridianClient('http://127.0.0.1:5100');
      const result = await client.ingestNodes(
        [createNode('Node1'), createNode('Node2')],
        {
          batchSize: 10,
          onError: (node, error) => {
            nodeErrors.push(`${node.id}:${String(error)}`);
          },
        },
      );

      expect(result).toEqual({
        successCount: 0,
        errorCount: 2,
      });
      expect(nodeErrors).toEqual([
        expect.stringContaining('Node1'),
        expect.stringContaining('Node2'),
      ]);
    } finally {
      globalThis.fetch = originalFetch;
    }
  });
});

function createNode(id: string): CodeNodeDto {
  return {
    id,
    name: id,
    type: 'File',
    filePath: `src/${id}.ts`,
    projectContext: 'CodeMeridian',
  };
}

function createEdge(sourceId: string, targetId: string): CodeEdgeDto {
  return {
    sourceId,
    targetId,
    type: 'Contains',
  };
}
