import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { readIndexerBatchFile } from '../../IndexerShared/dist/index.js';
import type { SqlAnalysisResult, SqlGraphSnapshot, SqlSettings, SqlSource } from './contracts.js';
import { SqlDialectRegistry } from './dialect-registry.js';
import { mapSqlGraph } from './graph-mapper.js';
import { selectSqlSource } from './settings.js';

export interface SqlRunOptions {
  root: string;
  project: string;
  serverUrl: string;
  batchFile: string;
  settings: SqlSettings;
  cacheFile: string;
  force?: boolean;
}

export class SqlIndexerApplication {
  constructor(private readonly registry: SqlDialectRegistry,
    private readonly publish: (snapshot: SqlGraphSnapshot, signal?: AbortSignal) => Promise<void>) {}

  async run(options: SqlRunOptions, signal?: AbortSignal): Promise<number> {
    const batch = readIndexerBatchFile(options.root, options.batchFile);
    if (batch.files.length > 10_000) throw new Error('SQL inventory exceeds 10,000 files.');
    let sourceBytes = 0;
    const groups = new Map<string, SqlSource[]>();
    for (const file of batch.files.sort()) {
      const relative = path.relative(options.root, file).replace(/\\/g, '/');
      if (relative.startsWith('../') || path.isAbsolute(relative) || !/\.sql$/i.test(relative))
        throw new Error(`Invalid SQL source path: ${relative}`);
      const selection = selectSqlSource(relative, options.settings);
      this.registry.resolve(selection.dialect);
      const bytes = fs.statSync(file).size;
      sourceBytes += bytes;
      if (bytes > 4 * 1024 * 1024 || sourceBytes > 64 * 1024 * 1024)
        throw new Error('SQL source budget exceeded (4 MiB per file, 64 MiB per scope).');
      const source = { path: relative, scope: selection.scope, text: fs.readFileSync(file, 'utf8'), fileRole: batch.fileRoles.get(relative) };
      const group = groups.get(selection.dialect) ?? [];
      group.push(source);
      groups.set(selection.dialect, group);
    }
    // Validate defaults even for an empty inventory, before permitting deletion.
    this.registry.resolve(options.settings.defaultDialect);
    const fingerprint = createHash('sha256').update(JSON.stringify({ contract: '1', settings: options.settings,
      defaultAnalyzer: this.registry.resolve(options.settings.defaultDialect).versionFingerprint,
      project: options.project, server: options.serverUrl,
      sources: [...groups].map(([dialect, sources]) => [this.registry.resolve(dialect).versionFingerprint, sources]) })).digest('hex');
    if (!options.force && fs.existsSync(options.cacheFile) && fs.readFileSync(options.cacheFile, 'utf8') === fingerprint) {
      console.log('SQL: unchanged source, configuration, and parser fingerprint.');
      return 0;
    }
    const snapshot: SqlGraphSnapshot = { contractVersion: '1', projectContext: options.project, files: [] };
    for (const [dialect, sources] of groups) {
      signal?.throwIfAborted();
      const result: SqlAnalysisResult = await this.registry.resolve(dialect).analyze(sources, signal);
      snapshot.files.push(...mapSqlGraph(options.project, result, options.settings).files);
    }
    signal?.throwIfAborted();
    await this.publish(snapshot, signal);
    const failed = snapshot.files.filter(f => f.status === 'failed');
    for (const file of snapshot.files) {
      console.log(`SQL ${file.status}: ${file.path} (${file.nodes.length} nodes, ${file.edges.length} edges)`);
      for (const diagnostic of file.diagnostics.slice(0, 5)) console.warn(`  ${diagnostic}`);
    }
    if (failed.length) return 1;
    signal?.throwIfAborted();
    fs.mkdirSync(path.dirname(options.cacheFile), { recursive: true });
    const temporary = `${options.cacheFile}.${process.pid}.tmp`;
    fs.writeFileSync(temporary, fingerprint);
    fs.renameSync(temporary, options.cacheFile);
    return 0;
  }
}
