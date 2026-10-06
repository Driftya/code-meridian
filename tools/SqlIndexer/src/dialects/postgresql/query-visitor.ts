import { walk } from '@pgsql/traverse';
import { deparseSync } from 'pgsql-parser';
import type { SqlDeclaration, SqlFileFacts, SqlName } from '../../contracts.js';
import { foreignKey, pgName, type PgRecord } from './declarations.js';

const queryTags = new Set(['SelectStmt', 'InsertStmt', 'UpdateStmt', 'DeleteStmt']);

export function extractReferences(stmt: PgRecord, facts: SqlFileFacts): void {
  const [tag, node] = Object.entries(stmt)[0] as [string, PgRecord];
  if (tag === 'ViewStmt') {
    query(node.query, new Set(), facts, facts.declarations.find(d => d.kind === 'DatabaseView' && d.offset === (node.view.location ?? 0)));
  } else if (queryTags.has(tag)) {
    query(stmt, new Set(), facts);
  } else if (tag === 'AlterTableStmt') {
    const name = pgName(node.relation);
    facts.references.push({ name, operation: 'Alters', offset: node.relation.location ?? 0 });
    const owner: SqlDeclaration = { name, kind: 'DatabaseTable', path: facts.source.path, scope: facts.source.scope, offset: node.relation.location ?? 0 };
    for (const cmd of node.cmds ?? []) {
      const item = cmd.AlterTableCmd;
      if (item?.subtype === 'AT_AddConstraint') foreignKey(item.def?.Constraint, owner, facts);
      else facts.diagnostics.push(`ALTER operation ${item?.subtype ?? 'unknown'} recorded without schema mutation simulation.`);
    }
  } else if (!['CreateStmt', 'CreateFunctionStmt', 'TransactionStmt'].includes(tag)) {
    facts.diagnostics.push(`Unsupported statement semantics: ${tag}.`);
  }
}

function query(stmt: PgRecord, outerCtes: Set<string>, facts: SqlFileFacts, owner?: SqlDeclaration): void {
  const [tag, node] = Object.entries(stmt)[0] as [string, PgRecord];
  const ctes = new Set(outerCtes);
  const withClause = node.withClause;
  if (withClause?.recursive) for (const c of withClause.ctes ?? []) ctes.add(c.CommonTableExpr.ctename);
  for (const c of withClause?.ctes ?? []) {
    const cte = c.CommonTableExpr;
    query(cte.ctequery, ctes, facts, owner);
    ctes.add(cte.ctename);
  }
  // The traversal library does not visit the untagged DML target RangeVar.
  if (tag !== 'SelectStmt' && node.relation) {
    reference(node.relation, 'Writes', ctes, facts, owner);
    if (tag === 'UpdateStmt' || tag === 'DeleteStmt') reference(node.relation, 'Reads', ctes, facts, owner);
  }
  walk({ [tag]: { ...node, withClause: undefined } }, path => {
    if (queryTags.has(path.tag) && path.parent !== null) {
      query({ [path.tag]: path.node }, ctes, facts, owner);
      return false;
    }
    if (path.tag === 'RangeVar') reference(path.node, 'Reads', ctes, facts, owner);
    if (path.tag === 'JoinExpr') join(path.node, ctes, facts, owner);
  });
}

function reference(range: PgRecord, operation: 'Reads' | 'Writes', ctes: Set<string>, facts: SqlFileFacts, owner?: SqlDeclaration): void {
  if (!range.schemaname && ctes.has(range.relname)) return;
  const temporary = facts.declarations.some(d => d.signature?.startsWith('temporary:') && d.name.name === range.relname
    && (!range.schemaname || range.schemaname === d.name.schema || range.schemaname === 'pg_temp'));
  facts.references.push({ name: pgName(range), operation, offset: range.location ?? 0, owner,
    reason: temporary ? 'temporary_object' : range.catalogname ? 'cross_database_name' : undefined });
}

function join(node: PgRecord, ctes: Set<string>, facts: SqlFileFacts, owner?: SqlDeclaration): void {
  const left = node.larg?.RangeVar;
  const right = node.rarg?.RangeVar;
  if (!left || !right || left.catalogname || right.catalogname
    || (!left.schemaname && ctes.has(left.relname)) || (!right.schemaname && ctes.has(right.relname))
    || facts.declarations.some(d => d.signature?.startsWith('temporary:')
      && ((!left.schemaname && d.name.name === left.relname) || (!right.schemaname && d.name.name === right.relname)))) {
    facts.diagnostics.push('Join operands include derived/CTE/nested relations; direct object join binding is unavailable.');
    return;
  }
  let predicate = '';
  try { if (node.quals) predicate = deparseSync(node.quals); } catch { /* Predicate formatting is optional. */ }
  const names: [SqlName, SqlName] = [pgName(left), pgName(right)];
  facts.references.push({ name: names[0], other: names[1], operation: 'JoinsWith', offset: right.location ?? left.location ?? 0, owner,
    properties: { kind: node.jointype, predicate: predicate.slice(0, 1024), predicateOrigin: 'normalized_ast',
      leftAlias: left.alias?.aliasname ?? left.relname, rightAlias: right.alias?.aliasname ?? right.relname } });
}
