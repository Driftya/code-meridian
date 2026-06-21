import type { CodeEdgeDto, CodeNodeDto, DocumentDto } from './types.js';

const DEFAULT_INGEST_CONCURRENCY = 8;
const DEFAULT_INGEST_BATCH_SIZE = 100;

export interface IngestBatchOptions<TItem> {
  batchSize?: number;
  concurrency?: number;
  onSuccess?: (item: TItem, processed: number, total: number) => void;
  onError?: (item: TItem, error: unknown, errorCount: number, total: number) => void;
}

export interface IngestBatchResult {
  successCount: number;
  errorCount: number;
}

export class CodeMeridianClient {
  constructor(
    private readonly baseUrl: string,
    private readonly apiKey?: string
  ) {}

  async ingestNode(node: CodeNodeDto): Promise<void> {
    await this.post('/api/v1/knowledge/nodes', node);
  }

  async ingestEdge(edge: CodeEdgeDto): Promise<void> {
    await this.post('/api/v1/knowledge/nodes/edges', edge);
  }

  async ingestNodes(
    nodes: readonly CodeNodeDto[],
    options: IngestBatchOptions<CodeNodeDto> = {},
  ): Promise<IngestBatchResult> {
    return await this.ingestMany(nodes, '/api/v1/knowledge/nodes/bulk', options);
  }

  async ingestEdges(
    edges: readonly CodeEdgeDto[],
    options: IngestBatchOptions<CodeEdgeDto> = {},
  ): Promise<IngestBatchResult> {
    return await this.ingestMany(edges, '/api/v1/knowledge/nodes/edges/bulk', options);
  }

  async ingestDocument(doc: DocumentDto): Promise<void> {
    await this.post('/api/v1/knowledge/documents', doc);
  }

  async generateEmbedding(text: string): Promise<number[] | null> {
    const res = await fetch(`${this.baseUrl}/api/v1/embeddings`, {
      method: 'POST',
      headers: this.headers({ 'Content-Type': 'application/json' }),
      body: JSON.stringify({ text }),
    });

    if (!res.ok) {
      return null;
    }

    const payload = await res.json() as { embedding?: number[] };
    return payload.embedding ?? null;
  }

  async isEmbeddingAvailable(): Promise<boolean> {
    const res = await fetch(`${this.baseUrl}/api/v1/embeddings/availability`, {
      method: 'GET',
      headers: this.headers(),
    });
    return res.ok;
  }

  async clearProject(projectContext: string): Promise<void> {
    const res = await fetch(
      `${this.baseUrl}/api/v1/knowledge/project/${encodeURIComponent(projectContext)}`,
      { method: 'DELETE', headers: this.headers() }
    );
    if (!res.ok && res.status !== 404) {
      throw new Error(`Clear failed: ${res.status} ${await res.text()}`);
    }
  }

  async clearCodeGraph(): Promise<void> {
    const res = await fetch(
      `${this.baseUrl}/api/v1/knowledge/code-graph`,
      { method: 'DELETE', headers: this.headers() }
    );
    if (!res.ok && res.status !== 404) {
      throw new Error(`Code graph clear failed: ${res.status} ${await res.text()}`);
    }
  }

  async deleteProjectFile(projectContext: string, filePath: string): Promise<void> {
    const normalized = filePath.replace(/\\/g, '/');
    const res = await fetch(
      `${this.baseUrl}/api/v1/knowledge/project/${encodeURIComponent(projectContext)}/files/${encodeURIComponent(normalized)}`,
      { method: 'DELETE', headers: this.headers() }
    );
    if (!res.ok && res.status !== 404) {
      throw new Error(`File cleanup failed: ${res.status} ${await res.text()}`);
    }
  }

  private async post(path: string, body: unknown): Promise<void> {
    const res = await fetch(`${this.baseUrl}${path}`, {
      method: 'POST',
      headers: this.headers({ 'Content-Type': 'application/json' }),
      body: JSON.stringify(body),
    });
    if (!res.ok) {
      throw new Error(`POST ${path} failed (${res.status}): ${await res.text()}`);
    }
  }

  private async ingestMany<TItem>(
    items: readonly TItem[],
    path: string,
    options: IngestBatchOptions<TItem>,
  ): Promise<IngestBatchResult> {
    if (items.length === 0) {
      return {
        successCount: 0,
        errorCount: 0,
      };
    }

    const concurrency = this.normalizeConcurrency(options.concurrency);
    const batchSize = this.normalizeBatchSize(options.batchSize);
    const batches = this.chunk(items, batchSize);
    let nextIndex = 0;
    let successCount = 0;
    let errorCount = 0;

    const worker = async (): Promise<void> => {
      while (true) {
        const currentIndex = nextIndex;
        nextIndex++;
        if (currentIndex >= batches.length) {
          return;
        }

        const batch = batches[currentIndex]!;
        try {
          await this.post(path, batch);
          for (const item of batch) {
            successCount++;
            options.onSuccess?.(item, successCount, items.length);
          }
        } catch (error) {
          for (const item of batch) {
            errorCount++;
            options.onError?.(item, error, errorCount, items.length);
          }
        }
      }
    };

    const workerCount = Math.min(concurrency, batches.length);
    await Promise.all(Array.from({ length: workerCount }, async () => await worker()));

    return {
      successCount,
      errorCount,
    };
  }

  private headers(base: Record<string, string> = {}): Record<string, string> {
    if (!this.apiKey) return base;
    return { ...base, Authorization: `Bearer ${this.apiKey}` };
  }

  private normalizeConcurrency(requested?: number): number {
    if (requested === undefined) {
      return DEFAULT_INGEST_CONCURRENCY;
    }

    if (!Number.isFinite(requested)) {
      return DEFAULT_INGEST_CONCURRENCY;
    }

    return Math.max(1, Math.floor(requested));
  }

  private normalizeBatchSize(requested?: number): number {
    if (requested === undefined) {
      return DEFAULT_INGEST_BATCH_SIZE;
    }

    if (!Number.isFinite(requested)) {
      return DEFAULT_INGEST_BATCH_SIZE;
    }

    return Math.max(1, Math.floor(requested));
  }

  private chunk<TItem>(items: readonly TItem[], batchSize: number): TItem[][] {
    const result: TItem[][] = [];
    for (let index = 0; index < items.length; index += batchSize) {
      result.push(items.slice(index, index + batchSize));
    }

    return result;
  }
}
