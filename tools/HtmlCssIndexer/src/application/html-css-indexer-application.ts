import { CodeMeridianClient, readIndexerBatchFile } from '@codemeridian/indexer-shared';
import type { ResolvedIndexCommandOptions } from '@codemeridian/indexer-shared';
import { walkFrontend } from '../walker.js';

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

    const { nodes, edges } = walkFrontend(
      options.rootPath,
      options.projectName,
      batch.files,
      relativePath => batch.fileRoles.get(relativePath),
    );

    console.log(`  Found ${nodes.length} nodes, ${edges.length} edges`);

    let nodeCount = 0;
    let nodeErrors = 0;
    for (const node of nodes) {
      try {
        await client.ingestNode(node);
        nodeCount++;
      } catch (error) {
        nodeErrors++;
        if (nodeErrors <= 5)
          console.warn(`  warn: node ${node.id}: ${error}`);
      }
    }
    console.log(`  Ingested ${nodeCount} nodes${nodeErrors > 0 ? ` (${nodeErrors} errors)` : ''}`);

    let edgeCount = 0;
    let edgeErrors = 0;
    for (const edge of edges) {
      try {
        await client.ingestEdge(edge);
        edgeCount++;
      } catch (error) {
        edgeErrors++;
        if (edgeErrors <= 5)
          console.warn(`  warn: edge ${edge.sourceId} -> ${edge.targetId}: ${error}`);
      }
    }
    console.log(`  Ingested ${edgeCount} edges${edgeErrors > 0 ? ` (${edgeErrors} errors)` : ''}`);

    console.log(`\nDone. '${options.projectName}' indexed into CodeMeridian at ${options.serverUrl}`);
  }
}
