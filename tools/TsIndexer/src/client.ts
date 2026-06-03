import type { CodeNodeDto, CodeEdgeDto, DocumentDto } from './types';

export class CodeMeridianClient {
  constructor(private readonly baseUrl: string) {}

  async ingestNode(node: CodeNodeDto): Promise<void> {
    await this.post('/api/v1/knowledge/nodes', node);
  }

  async ingestEdge(edge: CodeEdgeDto): Promise<void> {
    await this.post('/api/v1/knowledge/nodes/edges', edge);
  }

  async ingestDocument(doc: DocumentDto): Promise<void> {
    await this.post('/api/v1/knowledge/documents', doc);
  }

  async clearProject(projectContext: string): Promise<void> {
    const res = await fetch(
      `${this.baseUrl}/api/v1/knowledge/project/${encodeURIComponent(projectContext)}`,
      { method: 'DELETE' }
    );
    if (!res.ok && res.status !== 404) {
      throw new Error(`Clear failed: ${res.status} ${await res.text()}`);
    }
  }

  private async post(path: string, body: unknown): Promise<void> {
    const res = await fetch(`${this.baseUrl}${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    if (!res.ok) {
      throw new Error(`POST ${path} failed (${res.status}): ${await res.text()}`);
    }
  }
}
