import { loadModule, parse } from 'pgsql-parser';

/** Parser-owned types never cross the dialect analyzer boundary. */
export class PostgreSqlParserAdapter {
  async initialize(): Promise<void> { await loadModule(); }

  async parse(text: string): Promise<Awaited<ReturnType<typeof parse>>> {
    if (Buffer.byteLength(text, 'utf8') > 4 * 1024 * 1024) throw new Error('SQL file exceeds 4 MiB parsing limit.');
    return parse(text);
  }
}
