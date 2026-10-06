import type { SqlDialectAnalyzer } from './contracts.js';

export class SqlDialectRegistry {
  private readonly analyzers = new Map<string, SqlDialectAnalyzer>();

  constructor(analyzers: SqlDialectAnalyzer[]) {
    for (const analyzer of analyzers) {
      if (this.analyzers.has(analyzer.dialect)) throw new Error(`Duplicate SQL dialect: ${analyzer.dialect}`);
      this.analyzers.set(analyzer.dialect, analyzer);
    }
  }

  resolve(dialect: string): SqlDialectAnalyzer {
    const analyzer = this.analyzers.get(dialect);
    if (!analyzer) throw new Error(`Unsupported SQL dialect: ${dialect}`);
    return analyzer;
  }
}
