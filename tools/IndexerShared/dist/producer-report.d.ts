import type { WorkerIdentity } from './worker-identity.js';
export interface ProducerFileOutcome {
    path: string;
    sourceFingerprint: string | null;
    status: 'complete' | 'partial' | 'failed';
    diagnostics: string[];
    dialect?: string;
    databaseScope?: string;
}
/** Facts from the executing worker. Process exit is deliberately not a coverage claim. */
export declare class ProducerReport {
    readonly analyzer: string;
    readonly project: string;
    readonly rootPath: string;
    readonly fingerprint: WorkerIdentity['fingerprint'];
    readonly contractVersion = "1";
    readonly requestId: string;
    readonly scope: string;
    readonly files: ProducerFileOutcome[];
    readonly capabilities: Record<string, 'complete' | 'partial' | 'unknown' | 'unsupported'>;
    readonly limitations: string[];
    published: boolean;
    private readonly contents;
    constructor(analyzer: string, project: string, rootPath: string, fingerprint: WorkerIdentity['fingerprint']);
    capture(files: readonly string[]): void;
    read: (file: string) => string;
    mark(file: string, status: ProducerFileOutcome['status'], reason: string): void;
    write(): void;
}
