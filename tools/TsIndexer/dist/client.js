export class CodeMeridianClient {
    baseUrl;
    apiKey;
    constructor(baseUrl, apiKey) {
        this.baseUrl = baseUrl;
        this.apiKey = apiKey;
    }
    async ingestNode(node) {
        await this.post('/api/v1/knowledge/nodes', node);
    }
    async ingestEdge(edge) {
        await this.post('/api/v1/knowledge/nodes/edges', edge);
    }
    async ingestDocument(doc) {
        await this.post('/api/v1/knowledge/documents', doc);
    }
    async generateEmbedding(text) {
        const res = await fetch(`${this.baseUrl}/api/v1/embeddings`, {
            method: 'POST',
            headers: this.headers({ 'Content-Type': 'application/json' }),
            body: JSON.stringify({ text }),
        });
        if (!res.ok) {
            return null;
        }
        const payload = await res.json();
        return payload.embedding ?? null;
    }
    async isEmbeddingAvailable() {
        const res = await fetch(`${this.baseUrl}/api/v1/embeddings/availability`, {
            method: 'GET',
            headers: this.headers(),
        });
        return res.ok;
    }
    async clearProject(projectContext) {
        const res = await fetch(`${this.baseUrl}/api/v1/knowledge/project/${encodeURIComponent(projectContext)}`, { method: 'DELETE', headers: this.headers() });
        if (!res.ok && res.status !== 404) {
            throw new Error(`Clear failed: ${res.status} ${await res.text()}`);
        }
    }
    async clearCodeGraph() {
        const res = await fetch(`${this.baseUrl}/api/v1/knowledge/code-graph`, { method: 'DELETE', headers: this.headers() });
        if (!res.ok && res.status !== 404) {
            throw new Error(`Code graph clear failed: ${res.status} ${await res.text()}`);
        }
    }
    async deleteProjectFile(projectContext, filePath) {
        const normalized = filePath.replace(/\\/g, '/');
        const res = await fetch(`${this.baseUrl}/api/v1/knowledge/project/${encodeURIComponent(projectContext)}/files/${encodeURIComponent(normalized)}`, { method: 'DELETE', headers: this.headers() });
        if (!res.ok && res.status !== 404) {
            throw new Error(`File cleanup failed: ${res.status} ${await res.text()}`);
        }
    }
    async post(path, body) {
        const res = await fetch(`${this.baseUrl}${path}`, {
            method: 'POST',
            headers: this.headers({ 'Content-Type': 'application/json' }),
            body: JSON.stringify(body),
        });
        if (!res.ok) {
            throw new Error(`POST ${path} failed (${res.status}): ${await res.text()}`);
        }
    }
    headers(base = {}) {
        if (!this.apiKey)
            return base;
        return { ...base, Authorization: `Bearer ${this.apiKey}` };
    }
}
