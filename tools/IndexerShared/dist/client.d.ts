import type { CodeEdgeDto, CodeNodeDto, DocumentDto } from './types.js';
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
export declare class CodeMeridianClient {
    private readonly baseUrl;
    private readonly apiKey?;
    constructor(baseUrl: string, apiKey?: string | undefined);
    ingestNode(node: CodeNodeDto): Promise<void>;
    ingestEdge(edge: CodeEdgeDto): Promise<void>;
    ingestNodes(nodes: readonly CodeNodeDto[], options?: IngestBatchOptions<CodeNodeDto>): Promise<IngestBatchResult>;
    ingestEdges(edges: readonly CodeEdgeDto[], options?: IngestBatchOptions<CodeEdgeDto>): Promise<IngestBatchResult>;
    ingestDocument(doc: DocumentDto): Promise<void>;
    generateEmbedding(text: string): Promise<number[] | null>;
    isEmbeddingAvailable(): Promise<boolean>;
    clearProject(projectContext: string): Promise<void>;
    clearCodeGraph(): Promise<void>;
    deleteProjectFile(projectContext: string, filePath: string): Promise<void>;
    private post;
    private ingestMany;
    private headers;
    private normalizeConcurrency;
    private normalizeBatchSize;
    private chunk;
}
