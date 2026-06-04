import fetch from 'node-fetch';

export interface IEmbeddingProvider {
  generateEmbedding(text: string): Promise<number[] | null>;
  isAvailable(): Promise<boolean>;
  readonly dimensions: number;
  readonly providerName: string;
}

/**
 * No-op embedding provider - returns null embeddings.
 */
export class NoOpEmbeddingProvider implements IEmbeddingProvider {
  readonly dimensions = 0;
  readonly providerName = 'None';

  async generateEmbedding(): Promise<null> {
    return null;
  }

  async isAvailable(): Promise<boolean> {
    return false;
  }
}

/**
 * Stub embedding provider for testing - generates deterministic embeddings.
 */
export class StubEmbeddingProvider implements IEmbeddingProvider {
  readonly dimensions = 4;
  readonly providerName = 'Stub';
  private seed = 42;

  async generateEmbedding(text: string): Promise<number[] | null> {
    if (!text) return null;

    const embedding: number[] = [];
    for (let i = 0; i < this.dimensions; i++) {
      this.seed = (this.seed * 9301 + 49297) % 233280;
      embedding.push(this.seed / 233280 - 0.5);
    }
    return embedding;
  }

  async isAvailable(): Promise<boolean> {
    return true;
  }
}

/**
 * Ollama local embedding provider.
 * Uses a local Ollama instance to generate embeddings without API costs or external dependencies.
 * Models: llama2-uncased (384 dims), nomic-embed-text (768 dims), etc.
 */
export class OllamaEmbeddingProvider implements IEmbeddingProvider {
  readonly dimensions = 384; // llama2-uncased default
  readonly providerName = 'Ollama';
  private baseUrl: string;
  private model: string;

  constructor(baseUrl?: string, model?: string) {
    this.baseUrl = baseUrl || process.env.Embedding__OllamaBaseUrl || 'http://localhost:11434';
    this.model = model || process.env.Embedding__OllamaModel || 'llama2-uncased';
  }

  async generateEmbedding(text: string): Promise<number[] | null> {
    if (!text) return null;

    try {
      const response = await fetch(`${this.baseUrl}/api/embeddings`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          model: this.model,
          prompt: text,
        }),
      });

      if (!response.ok) {
        const error = await response.text();
        console.warn(`Ollama embedding request failed (${response.status}): ${error}`);
        return null;
      }

      const result = await response.json() as { embedding: number[] };
      return result.embedding ?? null;
    } catch (error) {
      console.error(`Error generating embedding via Ollama: ${error}`);
      return null;
    }
  }

  async isAvailable(): Promise<boolean> {
    try {
      // Health check: try to list available models
      const response = await fetch(`${this.baseUrl}/api/tags`);
      return response.ok;
    } catch (error) {
      console.warn(
        `Ollama server not available at ${this.baseUrl}. ` +
        'Ensure Ollama is running: ollama serve'
      );
      return false;
    }
  }
}

/**
 * OpenAI text-embedding-3-small provider.
 * Uses OpenAI API to generate 1536-dimensional embeddings.
 * Cost: $0.02 per 1M tokens
 */
export class OpenAiEmbeddingProvider implements IEmbeddingProvider {
  readonly dimensions = 1536;
  readonly providerName = 'OpenAI';
  private apiKey: string;
  private model: string;

  constructor(apiKey?: string, model = 'text-embedding-3-small') {
    this.apiKey = apiKey || process.env.Embedding__OpenAiApiKey || process.env.OPENAI_API_KEY || '';
    this.model = model || process.env.Embedding__OpenAiModel || 'text-embedding-3-small';
  }

  async generateEmbedding(text: string): Promise<number[] | null> {
    if (!text || !this.apiKey) return null;

    try {
      const response = await fetch('https://api.openai.com/v1/embeddings', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${this.apiKey}`,
        },
        body: JSON.stringify({
          input: text,
          model: this.model,
          encoding_format: 'float',
        }),
      });

      if (!response.ok) {
        const error = await response.text();
        console.warn(`OpenAI embedding request failed (${response.status}): ${error}`);
        return null;
      }

      const result = await response.json() as { data: { embedding: number[] }[] };
      return result.data?.[0]?.embedding ?? null;
    } catch (error) {
      console.error(`Error generating embedding via OpenAI: ${error}`);
      return null;
    }
  }

  async isAvailable(): Promise<boolean> {
    if (!this.apiKey) {
      console.warn('OpenAI embedding provider requires Embedding__OpenAiApiKey to be set');
      return false;
    }

    try {
      const embedding = await this.generateEmbedding('test');
      return embedding !== null;
    } catch {
      return false;
    }
  }
}

/**
 * Get the appropriate embedding provider based on environment variables.
 * Default: disabled. When enabled, uses Ollama (local, free) by default.
 */
export function getEmbeddingProvider(): IEmbeddingProvider {
  const enabled = process.env.Embedding__Enabled === 'true';
  if (!enabled) return new NoOpEmbeddingProvider();

  const provider = process.env.Embedding__Provider || 'Ollama';
  switch (provider) {
    case 'OpenAI':
      return new OpenAiEmbeddingProvider();
    case 'Ollama':
      return new OllamaEmbeddingProvider();
    case 'Stub':
      return new StubEmbeddingProvider();
    default:
      return new NoOpEmbeddingProvider();
  }
}
