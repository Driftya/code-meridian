import { parse, loadModule } from 'pgsql-parser';
import { createWorkerIdentity } from '../../IndexerShared/dist/index.js';

export async function workerIdentity() {
  await loadModule();
  const grammar = await parse('SELECT 1');
  if (!grammar.version) throw new Error('PostgreSQL grammar identity is unavailable.');
  return createWorkerIdentity('sql', import.meta.url,
    ['pgsql-parser', { name: 'libpg-query', from: 'pgsql-parser' }, '@pgsql/traverse'],
    { postgresqlGrammar: grammar.version });
}
