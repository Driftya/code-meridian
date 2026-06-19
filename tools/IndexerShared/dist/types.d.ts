export type CodeNodeType = 'Namespace' | 'Class' | 'Interface' | 'Method' | 'Property' | 'Field' | 'Enum' | 'File' | 'Module' | 'ExternalConcept' | 'DatabaseTable' | 'ApiEndpoint' | 'MessageTopic' | 'ExternalService' | 'Diagnostic' | 'ConfigurationFile' | 'ConfigurationKey' | 'ConfigurationEntry';
export type CodeEdgeType = 'Contains' | 'Calls' | 'Implements' | 'Inherits' | 'Uses' | 'DependsOn' | 'Overrides' | 'Reads' | 'Writes' | 'PublishesTo' | 'SubscribesTo' | 'DefinesConfig' | 'OverridesConfig' | 'ReadsConfig' | 'BindsConfig';
export interface CodeNodeDto {
    id: string;
    name: string;
    type: CodeNodeType;
    fileRole?: string;
    namespace?: string;
    filePath?: string;
    lineNumber?: number;
    lineCount?: number;
    summary?: string;
    sourceSnippet?: string;
    sourceHash?: string;
    projectContext: string;
    properties?: Record<string, string>;
    embeddingCsv?: string;
}
export interface CodeEdgeDto {
    sourceId: string;
    targetId: string;
    type: CodeEdgeType;
    isAsync?: boolean;
    callSite?: string;
    paramCount?: number;
    confidence?: number;
    properties?: Record<string, string>;
}
export interface DocumentDto {
    id?: string;
    content: string;
    source: string;
    projectContext: string;
}
