import { CodeMeridianClient, readIndexerBatchFile } from '../../../IndexerShared/dist/index.js';
import type { ResolvedIndexCommandOptions } from '../../../IndexerShared/dist/index.js';
import { walkFrontend } from '../walker.js';
import type { FrontendWalkProgress } from '../types.js';

const FILE_PROGRESS_INTERVAL = 10;
const INGEST_PROGRESS_INTERVAL = 500;
const INGEST_CONCURRENCY = 8;

export class HtmlCssIndexerApplication {
  async run(options: ResolvedIndexCommandOptions): Promise<number> {
    const client = new CodeMeridianClient(options.serverUrl, options.apiKey);
    await this.runIndexPass(options, client);
    return 0;
  }

  private async runIndexPass(
    options: ResolvedIndexCommandOptions,
    client: CodeMeridianClient,
  ): Promise<void> {
    console.log(`Indexing HTML/CSS/SCSS batch in ${options.rootPath}...`);

    const batch = readIndexerBatchFile(options.rootPath, options.batchFilePath);
    console.log(`  Batch size: ${batch.files.length} file(s)`);
    if (batch.files.length > 0) {
      console.log('  Building frontend graph...');
    }

    const { nodes, edges } = walkFrontend(
      options.rootPath,
      options.projectName,
      batch.files,
      relativePath => batch.fileRoles.get(relativePath),
      progress => this.logFileProgress(progress),
    );

    console.log(`  Found ${nodes.length} nodes, ${edges.length} edges`);
    if (nodes.length > 0) {
      console.log('  Uploading nodes...');
    }

    const nodeResult = await client.ingestNodes(nodes, {
      concurrency: INGEST_CONCURRENCY,
      onSuccess: (_node: unknown, processed: number, total: number) => {
        this.logIngestProgress('nodes', processed, total);
      },
      onError: (node: { id: string }, error: unknown, errorCount: number) => {
        if (errorCount <= 5)
          console.warn(`  warn: node ${node.id}: ${error}`);
      },
    });
    console.log(
      `  Ingested ${nodeResult.successCount} nodes${nodeResult.errorCount > 0 ? ` (${nodeResult.errorCount} errors)` : ''}`,
    );

    if (edges.length > 0) {
      console.log('  Uploading edges...');
    }
    const edgeResult = await client.ingestEdges(edges, {
      concurrency: INGEST_CONCURRENCY,
      onSuccess: (_edge: unknown, processed: number, total: number) => {
        this.logIngestProgress('edges', processed, total);
      },
      onError: (edge: { sourceId: string; targetId: string }, error: unknown, errorCount: number) => {
        if (errorCount <= 5)
          console.warn(`  warn: edge ${edge.sourceId} -> ${edge.targetId}: ${error}`);
      },
    });
    console.log(
      `  Ingested ${edgeResult.successCount} edges${edgeResult.errorCount > 0 ? ` (${edgeResult.errorCount} errors)` : ''}`,
    );

    console.log(`\nDone. '${options.projectName}' indexed into CodeMeridian at ${options.serverUrl}`);
  }

  private logFileProgress(progress: FrontendWalkProgress): void {
    if (
      progress.processedFiles === progress.totalFiles ||
      progress.processedFiles % FILE_PROGRESS_INTERVAL === 0
    ) {
      console.log(
        `  Processed ${progress.processedFiles}/${progress.totalFiles} frontend files (${progress.currentFile})`,
      );
    }
  }

  private logIngestProgress(kind: 'nodes' | 'edges', processed: number, total: number): void {
    if (processed === total || processed % INGEST_PROGRESS_INTERVAL === 0) {
      console.log(`  Uploaded ${processed}/${total} ${kind}`);
    }
  }
}
