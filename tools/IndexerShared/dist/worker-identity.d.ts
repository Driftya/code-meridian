export interface WorkerIdentity {
    contractVersion: '1';
    analyzer: string;
    fingerprint: {
        implementation: string;
        parser: string;
    };
    runtime: {
        versions: NodeJS.ProcessVersions;
        platform: string;
        architecture: string;
    };
    parsers: Array<{
        name: string;
        version: string;
        content: string;
    }>;
    parserRuntime: Record<string, string | number>;
}
/** Resolve dependencies from the worker (or its parser), never from the shared utility. */
export declare function createWorkerIdentity(analyzer: string, entryUrl: string, dependencies: ReadonlyArray<string | {
    name: string;
    from: string;
}>, parserRuntime?: Record<string, string | number>): WorkerIdentity;
/** Describe mode does not read repository settings, discover source files, or contact a server. */
export declare function describeWorker(identity: WorkerIdentity): boolean;
export declare function verifyWorkerIdentity(identity: WorkerIdentity, expected?: string | undefined): void;
