import { walk } from '@pgsql/traverse';
import type { SqlDeclaration, SqlFileFacts, SqlName } from '../../contracts.js';

// This record is private to the pinned PostgreSQL AST adapter. The upstream
// traversal API exposes node payloads as any; typed dialect facts leave this file.
export type PgRecord = Record<string, any>;
export function pgName(range: PgRecord): SqlName {
  return { schema: range.schemaname, name: range.relname };
}
export function strings(nodes: PgRecord[] = []): string[] { return nodes.map(n => n.String?.sval ?? ''); }

export function extractDeclarations(stmt: PgRecord, facts: SqlFileFacts): void {
  walk(stmt, {
    CreateStmt: path => {
      const n = path.node as PgRecord;
      const table = declaration(n.relation, 'DatabaseTable', facts);
      if (n.relation.relpersistence === 't') {
        facts.diagnostics.push('Temporary table identity is script-local; references are unresolved.');
        table.signature = `temporary:${facts.source.path}`;
        return false;
      }
      for (const element of n.tableElts ?? []) {
        const column = element.ColumnDef as PgRecord | undefined;
        if (column) {
          facts.declarations.push({ ...table, kind: 'DatabaseColumn', name: { schema: table.name.schema, name: column.colname },
            parent: table.name, offset: column.location ?? table.offset, signature: undefined });
          for (const constraint of column.constraints ?? []) foreignKey(constraint.Constraint, table, facts, [column.colname]);
        } else if (element.Constraint) foreignKey(element.Constraint, table, facts);
      }
      if (n.inhRelations?.length || n.partspec || n.ofTypename)
        facts.diagnostics.push('Table inheritance, partitioning, or typed-table dependencies are not analyzed.');
      return false;
    },
    ViewStmt: path => { declaration(path.node.view, 'DatabaseView', facts); return false; },
    CreateFunctionStmt: path => {
      const n = path.node as PgRecord;
      const names = strings(n.funcname);
      const functionName = { relname: names.at(-1), schemaname: names.length > 1 ? names.at(-2) : undefined, location: n.location ?? 0 };
      const fn = declaration(functionName, 'DatabaseFunction', facts);
      fn.signature = JSON.stringify((n.parameters ?? []).map((p: PgRecord) => p.FunctionParameter ?? p)
        .filter((p: PgRecord) => !['FUNC_PARAM_OUT', 'FUNC_PARAM_TABLE'].includes(p.mode))
        .map((p: PgRecord) => ({ names: strings(p.argType?.names), arrays: p.argType?.arrayBounds?.length ?? 0 })));
      facts.diagnostics.push('Function declaration indexed; function body dependencies are not analyzed.');
      return false;
    }
  });
}

function declaration(range: PgRecord, kind: SqlDeclaration['kind'], facts: SqlFileFacts): SqlDeclaration {
  const item: SqlDeclaration = { name: pgName(range), kind, scope: facts.source.scope, path: facts.source.path, offset: range.location ?? 0 };
  facts.declarations.push(item);
  return item;
}

export function foreignKey(n: PgRecord, table: SqlDeclaration, facts: SqlFileFacts, inlineColumns?: string[]): void {
  if (n?.contype !== 'CONSTR_FOREIGN' || !n.pktable) return;
  facts.references.push({ name: pgName(n.pktable), operation: 'References', offset: n.location ?? table.offset, owner: table });
  const from = inlineColumns ?? strings(n.fk_attrs);
  const to = strings(n.pk_attrs);
  if (from.length !== to.length || !to.length) {
    facts.diagnostics.push('Foreign key target columns omitted or unavailable; column binding is unresolved.');
    return;
  }
  for (let i = 0; i < from.length; i++) {
    facts.references.push({ name: pgName(n.pktable), operation: 'References', offset: n.location ?? table.offset, owner: table,
      properties: { sourceColumn: from[i], targetColumn: to[i] } });
  }
}
