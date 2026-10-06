import fs from 'node:fs';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { CodeMeridianClient, loadEnvironmentForInvocation } from '../../IndexerShared/dist/index.js';
import { SqlIndexerApplication } from './application.js';
import { SqlDialectRegistry } from './dialect-registry.js';
import { PostgreSqlDialectAnalyzer } from './dialects/postgresql/analyzer.js';
import type { SqlSettings } from './contracts.js';

const controller = new AbortController();
process.once('SIGINT', () => controller.abort());
process.once('SIGTERM', () => controller.abort());
try {
  const { values, positionals } = parseArgs({ allowPositionals: true, options: {
    project: { type: 'string' }, url: { type: 'string' }, 'batch-file': { type: 'string' },
    'settings-file': { type: 'string' }, 'cache-file': { type: 'string' }, force: { type: 'boolean' },
  } });
  if (!positionals[0] || !values.project || !values.url || !values['batch-file'] || !values['settings-file'] || !values['cache-file'])
    throw new Error('SQL worker requires root, --project, --url, --batch-file, --settings-file, and --cache-file.');
  const root = path.resolve(positionals[0]);
  loadEnvironmentForInvocation(root);
  const client = new CodeMeridianClient(values.url, process.env.CODEMERIDIAN_API_KEY);
  const application = new SqlIndexerApplication(new SqlDialectRegistry([new PostgreSqlDialectAnalyzer()]),
    (snapshot, signal) => client.publishSqlGraph(snapshot, signal));
  process.exitCode = await application.run({ root, project: values.project, serverUrl: values.url,
    batchFile: values['batch-file'], settings: JSON.parse(fs.readFileSync(values['settings-file'], 'utf8')) as SqlSettings,
    cacheFile: values['cache-file'], force: values.force }, controller.signal);
} catch (error) {
  console.error(`SQL indexing failed: ${error instanceof Error ? error.message : String(error)}`);
  process.exitCode = 1;
}
