import type { SqlAnalysisResult, SqlDialectAnalyzer, SqlFileFacts, SqlSource } from '../../contracts.js';
import { PostgreSqlParserAdapter } from './parser-adapter.js';
import { extractDeclarations, type PgRecord } from './declarations.js';
import { extractReferences } from './query-visitor.js';

export class PostgreSqlDialectAnalyzer implements SqlDialectAnalyzer {
  readonly dialect = 'postgresql';
  readonly versionFingerprint = 'postgresql.v1:pgsql-parser18.2.8:traverse18.0.0:contract1';
  readonly capabilities = ['tables', 'columns', 'views', 'foreign_keys', 'select', 'joins', 'insert', 'update', 'delete', 'alter', 'function_declarations'];

  constructor(private readonly parser = new PostgreSqlParserAdapter()) {}

  async analyze(sources: SqlSource[], signal?: AbortSignal): Promise<SqlAnalysisResult> {
    await this.parser.initialize(); // Initialization failures fail the run, not individual files.
    const files: SqlFileFacts[] = [];
    let grammarVersion = 0;
    for (const source of sources) {
      signal?.throwIfAborted();
      const facts: SqlFileFacts = { source, declarations: [], references: [], diagnostics: [], status: 'complete' };
      try {
        const ast = await this.parser.parse(source.text);
        grammarVersion = ast.version ?? 0;
        for (const statement of ast.stmts ?? []) extractDeclarations(statement.stmt as PgRecord, facts);
        for (const statement of ast.stmts ?? []) extractReferences(statement.stmt as PgRecord, facts);
        if (facts.diagnostics.length) facts.status = 'partial';
      } catch (error) {
        facts.status = 'failed';
        facts.declarations = [];
        facts.references = [];
        facts.diagnostics.push((error instanceof Error ? error.message : String(error)).slice(0, 2048));
      }
      facts.diagnostics = [...new Set(facts.diagnostics)].slice(0, 100);
      files.push(facts);
    }
    return { contractVersion: '1', dialect: this.dialect, files, grammarVersion, parser: 'pgsql-parser@18.2.8' };
  }
}
