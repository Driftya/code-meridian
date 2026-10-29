import { CodeMeridianClient, readIndexerBatchFile } from '../../../IndexerShared/dist/index.js';
import type { ResolvedIndexCommandOptions, ProducerReport } from '../../../IndexerShared/dist/index.js';
import { walkFrontend } from '../walker.js';
import type { FrontendWalkProgress } from '../types.js';

const FILE_PROGRESS_INTERVAL = 10;
const INGEST_PROGRESS_INTERVAL = 500;
const INGEST_CONCURRENCY = 8;

export class HtmlCssIndexerApplication {
  async run(options: ResolvedIndexCommandOptions, report?: ProducerReport): Promise<number> {
    const client = new CodeMeridianClient(options.serverUrl, options.apiKey);
    return await this.runIndexPass(options, client, report);
  }

  private async runIndexPass(
    options: ResolvedIndexCommandOptions,
    client: CodeMeridianClient,
    report?: ProducerReport,
  ): Promise<number> {
    console.log(`Indexing HTML/CSS/SCSS batch in ${options.rootPath}...`);

    const batch = readIndexerBatchFile(options.rootPath, options.batchFilePath);
    report?.capture(batch.files);
    console.log(`  Batch size: ${batch.files.length} file(s)`);
    if (batch.files.length > 0) {
      console.log('  Building frontend graph...');
    }

    const { nodes, edges, failedFiles } = walkFrontend(
      options.rootPath,
      options.projectName,
      batch.files,
      relativePath => batch.fileRoles.get(relativePath),
      progress => this.logFileProgress(progress),
      report?.read,
      file => report?.mark(file, 'failed', 'frontend_parse_failed'),
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
    if (nodeResult.errorCount > 0 || nodeResult.successCount !== nodes.length) return 1;

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
    if (edgeResult.errorCount > 0 || edgeResult.successCount !== edges.length) return 1;
    if (report) {
      report.published = true;
      report.capabilities.frontend_relationships = failedFiles.length ? 'partial' : 'unknown';
      report.capabilities.frontend_cascade = 'unsupported';
      report.limitations.push('Static regex/import ownership and replacement are unaudited; relationships do not establish complete selector or cascade coverage.');
    }

    console.log(`\nDone. '${options.projectName}' indexed into CodeMeridian at ${options.serverUrl}`);
    return failedFiles.length ? 2 : 0;
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
