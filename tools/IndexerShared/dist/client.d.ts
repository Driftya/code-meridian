import type { CodeEdgeDto, CodeNodeDto, DocumentDto } from './types.js';
export declare class CodeMeridianClient {
    private readonly baseUrl;
    private readonly apiKey?;
    constructor(baseUrl: string, apiKey?: string | undefined);
    ingestNode(node: CodeNodeDto): Promise<void>;
    ingestEdge(edge: CodeEdgeDto): Promise<void>;
    ingestDocument(doc: DocumentDto): Promise<void>;
    generateEmbedding(text: string): Promise<number[] | null>;
    isEmbeddingAvailable(): Promise<boolean>;
    clearProject(projectContext: string): Promise<void>;
    clearCodeGraph(): Promise<void>;
    deleteProjectFile(projectContext: string, filePath: string): Promise<void>;
    private post;
    private headers;
}
