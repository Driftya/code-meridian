import { CodeMeridianClient, readIndexerBatchFile } from '#indexer-shared';
import type { ResolvedIndexCommandOptions } from '#indexer-shared';
import { walkFrontend } from '../walker.js';
import type { FrontendWalkProgress } from '../types.js';

const FILE_PROGRESS_INTERVAL = 10;
const INGEST_PROGRESS_INTERVAL = 500;

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

    let nodeCount = 0;
    let nodeErrors = 0;
    for (const node of nodes) {
      try {
        await client.ingestNode(node);
        nodeCount++;
        this.logIngestProgress('nodes', nodeCount, nodes.length);
      } catch (error) {
        nodeErrors++;
        if (nodeErrors <= 5)
          console.warn(`  warn: node ${node.id}: ${error}`);
      }
    }
    console.log(`  Ingested ${nodeCount} nodes${nodeErrors > 0 ? ` (${nodeErrors} errors)` : ''}`);

    if (edges.length > 0) {
      console.log('  Uploading edges...');
    }
    let edgeCount = 0;
    let edgeErrors = 0;
    for (const edge of edges) {
      try {
        await client.ingestEdge(edge);
        edgeCount++;
        this.logIngestProgress('edges', edgeCount, edges.length);
      } catch (error) {
        edgeErrors++;
        if (edgeErrors <= 5)
          console.warn(`  warn: edge ${edge.sourceId} -> ${edge.targetId}: ${error}`);
      }
    }
    console.log(`  Ingested ${edgeCount} edges${edgeErrors > 0 ? ` (${edgeErrors} errors)` : ''}`);

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
