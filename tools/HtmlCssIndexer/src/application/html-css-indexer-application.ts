import { readIndexerBatchFile } from '@codemeridian/indexer-shared';
import type { ResolvedIndexCommandOptions } from '@codemeridian/indexer-shared';

export class HtmlCssIndexerApplication {
  async run(options: ResolvedIndexCommandOptions): Promise<number> {
    console.log(`Indexing HTML/CSS/SCSS batch in ${options.rootPath}...`);

    const batch = readIndexerBatchFile(options.rootPath, options.batchFilePath);
    console.log(`  Batch size: ${batch.files.length} file(s)`);
    console.log('  HTML/CSS/SCSS relationship extraction is not implemented yet.');
    console.log(`\nDone. Placeholder worker executed for '${options.projectName}' against ${options.serverUrl}`);

    return 0;
  }
}
