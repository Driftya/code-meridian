import type { CodeNodeDto, CodeEdgeDto, DocumentDto } from './types.js';

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

  private headers(base: Record<string, string> = {}): Record<string, string> {
    if (!this.apiKey) return base;
    return { ...base, Authorization: `Bearer ${this.apiKey}` };
  }
}
