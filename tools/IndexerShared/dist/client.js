const DEFAULT_INGEST_CONCURRENCY = 8;
const DEFAULT_INGEST_BATCH_SIZE = 100;
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
    async ingestNodes(nodes, options = {}) {
        return await this.ingestMany(nodes, '/api/v1/knowledge/nodes/bulk', options);
    }
    async ingestEdges(edges, options = {}) {
        return await this.ingestMany(edges, '/api/v1/knowledge/nodes/edges/bulk', options);
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
    async ingestMany(items, path, options) {
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
        const worker = async () => {
            while (true) {
                const currentIndex = nextIndex;
                nextIndex++;
                if (currentIndex >= batches.length) {
                    return;
                }
                const batch = batches[currentIndex];
                try {
                    await this.post(path, batch);
                    for (const item of batch) {
                        successCount++;
                        options.onSuccess?.(item, successCount, items.length);
                    }
                }
                catch (error) {
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
    headers(base = {}) {
        if (!this.apiKey)
            return base;
        return { ...base, Authorization: `Bearer ${this.apiKey}` };
    }
    normalizeConcurrency(requested) {
        if (requested === undefined) {
            return DEFAULT_INGEST_CONCURRENCY;
        }
        if (!Number.isFinite(requested)) {
            return DEFAULT_INGEST_CONCURRENCY;
        }
        return Math.max(1, Math.floor(requested));
    }
    normalizeBatchSize(requested) {
        if (requested === undefined) {
            return DEFAULT_INGEST_BATCH_SIZE;
        }
        if (!Number.isFinite(requested)) {
            return DEFAULT_INGEST_BATCH_SIZE;
        }
        return Math.max(1, Math.floor(requested));
    }
    chunk(items, batchSize) {
        const result = [];
        for (let index = 0; index < items.length; index += batchSize) {
            result.push(items.slice(index, index + batchSize));
        }
        return result;
    }
}
