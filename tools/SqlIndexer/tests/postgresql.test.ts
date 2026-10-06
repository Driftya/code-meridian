import { describe, it, expect } from 'vitest';
import { PostgreSqlDialectAnalyzer } from '../src/dialects/postgresql/analyzer.js';
import { mapSqlGraph } from '../src/graph-mapper.js';
import { sourceLocation } from '../src/source-location.js';
import { SqlDialectRegistry } from '../src/dialect-registry.js';
import { selectSqlSource } from '../src/settings.js';

const settings = { enabled: true, defaultDialect: 'postgresql', databaseScope: 'main' };
async function graph(text: string, searchPath?: string[]) {
  const analysis = await new PostgreSqlDialectAnalyzer().analyze([{ path: 'queries/test.sql', text, scope: 'main' }]);
  return mapSqlGraph('Test', analysis, { ...settings, searchPath }).files[0];
}

describe('native PostgreSQL semantics', () => {
  it('extracts declarations, view dependencies, foreign keys and mixed reads/writes', async () => {
    const result = await graph(`CREATE TABLE app.customers(id uuid PRIMARY KEY);
      CREATE TABLE app.orders(id uuid, customer_id uuid REFERENCES app.customers(id));
      CREATE VIEW app.v AS SELECT o.id FROM app.orders o LEFT JOIN app.customers c ON o.customer_id=c.id;
      INSERT INTO app.orders SELECT * FROM app.old; UPDATE app.orders SET id=null FROM app.old;
      DELETE FROM app.orders USING app.old; ALTER TABLE app.orders ADD CONSTRAINT fk FOREIGN KEY(id) REFERENCES app.customers(id);`);
    const named = (id: string) => result.nodes.find(n => n.id === id)!.name;
    expect(result.status).toBe('complete');
    expect(result.edges.some(e => e.type === 'Writes' && named(e.targetId) === 'app.orders')).toBe(true);
    expect(result.edges.some(e => e.type === 'Reads' && named(e.targetId) === 'app.old')).toBe(true);
    expect(result.edges.some(e => e.type === 'DependsOn' && named(e.sourceId) === 'app.v')).toBe(true);
    expect(result.edges.filter(e => e.type === 'References').length).toBe(4);
    expect(result.edges.find(e => e.type === 'JoinsWith')?.properties?.kind).toBe('JOIN_LEFT');
    expect(result.edges.some(e => e.type === 'Alters')).toBe(true);
  });

  it('respects sequential CTE visibility, shadowing and writable CTE targets', async () => {
    const result = await graph('WITH orders AS (SELECT * FROM app.orders), changed AS (DELETE FROM app.old RETURNING *) SELECT * FROM orders;');
    expect(result.nodes.filter(n => n.type === 'DatabaseRelation').map(n => n.name).sort()).toEqual(['app.old', 'app.orders']);
    expect(result.edges.some(e => e.type === 'Writes')).toBe(true);
    expect(result.nodes.some(n => n.type === 'SqlReference')).toBe(false);
    const recursive = await graph('WITH RECURSIVE c AS (SELECT * FROM app.seed UNION ALL SELECT * FROM c) SELECT * FROM c;');
    expect(recursive.nodes.filter(n => n.type === 'DatabaseRelation').map(n => n.name)).toEqual(['app.seed']);
  });

  it('keeps quoted case and dotted names distinct and reports ambiguous search paths', async () => {
    const result = await graph('SELECT * FROM "A.B"."Orders"; SELECT * FROM a.b; SELECT * FROM orders;');
    expect(result.nodes.filter(n => n.type === 'DatabaseRelation')).toHaveLength(2);
    expect(result.nodes.find(n => n.type === 'SqlReference')?.properties?.reason).toBe('unknown_search_path');
    expect((await graph('SELECT * FROM orders;', ['app', 'other'])).status).toBe('partial');
    expect((await graph('SELECT * FROM orders;', ['app'])).status).toBe('complete');
  });

  it('preserves repeated join sites, aliases and source positions', async () => {
    const result = await graph('SELECT * FROM app.t a JOIN app.t b ON a.id=b.id;\nSELECT * FROM app.t x JOIN app.t y ON x.id=y.id;');
    const joins = result.edges.filter(e => e.type === 'JoinsWith');
    expect(joins).toHaveLength(2);
    expect(joins[0].properties?.occurrence).not.toBe(joins[1].properties?.occurrence);
    expect(joins.map(e => e.sourceLine)).toEqual([1, 2]);
    expect(joins.map(e => e.properties?.rightAlias)).toEqual(['b', 'y']);
    const sql = '-- å🙂\nSELECT * FROM app.t;';
    const unicode = await graph(sql);
    expect(unicode.edges.find(e => e.type === 'Reads')?.sourceLine).toBe(2);
    expect(sourceLocation('å🙂 x', Buffer.byteLength('å🙂 ', 'utf8'))).toEqual({ line: 1, column: 5 });
  });

  it('keeps contradictory migration declarations neutral instead of choosing a table or view', async () => {
    const result = await graph('CREATE TABLE app.t(id int); CREATE VIEW app.t AS SELECT 1; SELECT * FROM app.t;');
    expect(result.status).toBe('partial');
    expect(result.nodes.find(n => n.name === 'app.t')?.type).toBe('DatabaseRelation');
    expect(result.nodes.find(n => n.type === 'SqlReference')?.properties?.reason).toBe('conflicting_declarations');
    expect(result.edges.some(e => e.type === 'Reads')).toBe(false);
  });

  it('does not split dollar-quoted bodies and reports body, temp and omitted-key limits', async () => {
    const result = await graph("CREATE FUNCTION app.f() RETURNS void AS $$ BEGIN EXECUTE 'SELECT 1;'; END; $$ LANGUAGE plpgsql; CREATE TEMP TABLE t(id int); SELECT * FROM t;");
    expect(result.nodes.filter(n => n.type === 'DatabaseFunction')).toHaveLength(1);
    expect(result.status).toBe('partial');
    expect(result.nodes.some(n => n.type === 'SqlReference' && n.properties?.reason === 'temporary_object')).toBe(true);
    expect((await graph('CREATE TABLE app.t(id int REFERENCES app.other);')).diagnostics.join(' ')).toContain('columns omitted');
  });

  it('contains per-file syntax errors and honors cancellation', async () => {
    const analyzer = new PostgreSqlDialectAnalyzer();
    const result = await analyzer.analyze([{ path: 'bad.sql', scope: 'main', text: 'SELECT FROM !' }, { path: 'ok.sql', scope: 'main', text: 'SELECT * FROM app.ok;' }]);
    expect(result.grammarVersion).toBe(180004);
    expect(result.files.map(f => f.status)).toEqual(['failed', 'complete']);
    const controller = new AbortController(); controller.abort();
    await expect(analyzer.analyze([{ path: 'ok.sql', scope: 'main', text: 'SELECT 1;' }], controller.signal)).rejects.toThrow();
  });
});

describe('dialect extension and selection', () => {
  it('maps another analyzer through the same fact contract without PostgreSQL ASTs', async () => {
    const analyzer = {
      dialect: 'example', versionFingerprint: 'example.v1', capabilities: ['select'],
      analyze: async (sources: import('../src/contracts.js').SqlSource[]): Promise<import('../src/contracts.js').SqlAnalysisResult> => ({
        contractVersion: '1', dialect: 'example', parser: 'example-parser.v1', grammarVersion: 1,
        files: sources.map(source => ({ source, declarations: [], diagnostics: [], status: 'complete',
          references: [{ name: { schema: 'app', name: 't' }, operation: 'Reads', offset: 0 }] }))
      })
    };
    const result = await new SqlDialectRegistry([analyzer]).resolve('example').analyze([{ path: 'example.sql', scope: 'main', text: 'fixture' }]);
    const mapped = mapSqlGraph('Test', result, settings).files[0];
    expect(mapped.edges.some(e => e.type === 'Reads')).toBe(true);
    expect(mapped.nodes.every(n => n.properties?.dialect === 'example')).toBe(true);
  });

  it('uses stable overload identities and matches UTF-16 repository source columns', async () => {
    const first = await graph('CREATE FUNCTION app.f(value integer) RETURNS int LANGUAGE SQL AS $$ SELECT 1 $$;');
    const moved = await graph('\n\nCREATE FUNCTION app.f(renamed integer) RETURNS int LANGUAGE SQL AS $$ SELECT 2 $$;');
    expect(first.nodes.find(n => n.type === 'DatabaseFunction')?.id).toBe(moved.nodes.find(n => n.type === 'DatabaseFunction')?.id);
    const overloaded = await graph('CREATE FUNCTION app.f(value text) RETURNS int LANGUAGE SQL AS $$ SELECT 1 $$;');
    expect(first.nodes.find(n => n.type === 'DatabaseFunction')?.id).not.toBe(overloaded.nodes.find(n => n.type === 'DatabaseFunction')?.id);
    const sql = "SELECT 'å🙂' FROM app.t;";
    expect((await graph(sql)).edges.find(e => e.type === 'Reads')?.sourceColumn).toBe(sql.indexOf('app.t') + 1);
  });
  it('rejects unknown and duplicate analyzers and conflicting source rules', () => {
    const analyzer = new PostgreSqlDialectAnalyzer();
    expect(() => new SqlDialectRegistry([analyzer, analyzer])).toThrow('Duplicate');
    expect(() => new SqlDialectRegistry([analyzer]).resolve('mysql')).toThrow('Unsupported');
    expect(selectSqlSource('migrations/a.sql', { ...settings, sources: [{ pattern: '**/*.sql', dialect: 'postgresql', databaseScope: 'other' }] }).scope).toBe('other');
    expect(() => selectSqlSource('a.sql', { ...settings, sources: [{ pattern: '**/*.sql', dialect: 'postgresql' }, { pattern: '*.sql', dialect: 'postgresql' }] })).toThrow('Conflicting');
  });
});
