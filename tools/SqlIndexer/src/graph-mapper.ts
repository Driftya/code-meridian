import { createHash } from 'node:crypto';
import type { CodeEdgeDto, CodeNodeDto } from '../../IndexerShared/dist/index.js';
import type { SqlAnalysisResult, SqlDeclaration, SqlFileGraph, SqlGraphSnapshot, SqlName, SqlSettings } from './contracts.js';
import { SqlSourcePositionMap } from './source-location.js';

const hash = (value: unknown): string => createHash('sha256').update(JSON.stringify(value)).digest('hex');

export function mapSqlGraph(project: string, analysis: SqlAnalysisResult, settings: SqlSettings): SqlGraphSnapshot {
  const prefix = `${project}::Sql::`;
  const objectId = (scope: string, name: SqlName, family = 'relation', extra?: unknown): string =>
    prefix + 'Object::' + hash([analysis.dialect, scope, family, name.schema ?? null, name.name, extra ?? null]);
  const effectiveName = (name: SqlName): SqlName => ({ ...name, schema: name.schema ?? settings.searchPath?.[0] });
  const catalog = analysis.files.flatMap(f => f.declarations).filter(d => !d.signature?.startsWith('temporary:'));
  const nameParts = (name: SqlName): (string | null)[] => [name.schema ?? null, name.name];
  const catalogKey = (scope: string, name: SqlName): string => JSON.stringify([scope, ...nameParts(name)]);
  const relationKinds = new Map<string, Set<SqlDeclaration['kind']>>();
  for (const declaration of catalog.filter(d => ['DatabaseTable', 'DatabaseView'].includes(d.kind))) {
    const key = catalogKey(declaration.scope, effectiveName(declaration.name));
    const kinds = relationKinds.get(key) ?? new Set<SqlDeclaration['kind']>();
    kinds.add(declaration.kind); relationKinds.set(key, kinds);
  }
  const declarationId = (d: SqlDeclaration): string => objectId(d.scope, effectiveName(d.name),
    d.kind === 'DatabaseColumn' ? 'column' : d.kind === 'DatabaseFunction' ? 'function' : 'relation',
    d.kind === 'DatabaseColumn' ? nameParts(effectiveName(d.parent!)) : d.signature);

  const resolve = (scope: string, name: SqlName): { id?: string; name: SqlName; kind?: CodeNodeDto['type']; reason?: string } => {
    if (!name.schema && settings.searchPath?.length !== 1) return { name, reason: 'unknown_search_path' };
    const qualified = effectiveName(name);
    const kinds = relationKinds.get(catalogKey(scope, qualified)) ?? new Set<SqlDeclaration['kind']>();
    if (kinds.size > 1) return { name: qualified, reason: 'conflicting_declarations' };
    return { id: objectId(scope, qualified), name: qualified, kind: [...kinds][0] ?? 'DatabaseRelation' };
  };

  const files: SqlFileGraph[] = analysis.files.map(facts => {
    const { source } = facts;
    const positions = new SqlSourcePositionMap(source.text);
    const sourceHash = createHash('sha256').update(source.text, 'utf8').digest('hex');
    const fileId = prefix + 'File::' + hash(source.path);
    const nodes = new Map<string, CodeNodeDto>();
    const edges: CodeEdgeDto[] = [];
    const diagnostics = [...facts.diagnostics];
    const properties = { language: 'sql', dialect: analysis.dialect, databaseScope: source.scope,
      parser: analysis.parser, grammarVersion: String(analysis.grammarVersion) };
    const ownedNode = (id: string, name: string, type: CodeNodeDto['type'], offset: number, extra: Record<string, string> = {}): void => {
      const location = positions.locate(offset);
      nodes.set(id, { id, name, type, projectContext: project, filePath: source.path, fileRole: source.fileRole,
        lineNumber: location.line, lineCount: 1, sourceHash, properties: { ...properties, ...extra } });
    };
    const sharedNode = (id: string, name: SqlName, type: CodeNodeDto['type'], extra: Record<string, string> = {}): void => {
      nodes.set(id, { id, name: name.schema ? `${name.schema}.${name.name}` : name.name, type,
        namespace: name.schema, projectContext: project, properties: { ...properties, ...extra } });
    };
    const edge = (from: string, to: string, type: CodeEdgeDto['type'], offset: number, extra: Record<string, string> = {}): void => {
      const location = positions.locate(offset);
      edges.push({ sourceId: from, targetId: to, type, sourceFilePath: source.path, sourceLine: location.line,
        sourceColumn: location.column, callSite: `${source.path}:${location.line}:${location.column}`, confidence: 1,
        evidenceKind: 'extracted', evidenceReason: 'sql_static_syntax', resolver: `${analysis.dialect}.v1`,
        properties: { ...extra, occurrence: hash([source.path, offset, type, from, to, edges.length]) } });
    };
    ownedNode(fileId, source.path, 'File', 0);
    nodes.get(fileId)!.lineCount = source.text.split('\n').length;
    if (facts.status !== 'failed') {
      for (const d of facts.declarations) {
        if (d.signature?.startsWith('temporary:')) continue;
        const id = declarationId(d);
        const name = effectiveName(d.name);
        const conflicting = relationKinds.get(catalogKey(d.scope, name))?.size === 2;
        const kind = conflicting && ['DatabaseTable', 'DatabaseView'].includes(d.kind) ? 'DatabaseRelation' : d.kind;
        sharedNode(id, name, kind, d.signature ? { signature: d.signature } : {});
        if (conflicting) diagnostics.push(`Conflicting relation declarations for ${name.schema ? name.schema + '.' : ''}${name.name}.`);
        const site = prefix + 'Declaration::' + hash([source.path, d.offset, d.kind, id]);
        ownedNode(site, name.name, 'SqlDeclaration', d.offset);
        edge(fileId, site, 'Contains', d.offset);
        edge(site, id, 'Declares', d.offset);
        if (d.parent) {
          const parentId = objectId(source.scope, effectiveName(d.parent));
          const parentName = effectiveName(d.parent);
          sharedNode(parentId, parentName, relationKinds.get(catalogKey(d.scope, parentName))?.size === 2 ? 'DatabaseRelation' : 'DatabaseTable');
          nodes.get(id)!.name = `${nodes.get(parentId)!.name}.${d.name.name}`;
          nodes.get(id)!.properties!.parentObject = parentId;
          edge(parentId, id, 'Contains', d.offset);
        }
      }
      for (const ref of facts.references) {
        const resolved = resolve(source.scope, ref.name);
        const other = ref.other ? resolve(source.scope, ref.other) : undefined;
        const reason = ref.reason ?? resolved.reason ?? other?.reason;
        if (reason || !resolved.id) {
          const site = prefix + 'Reference::' + hash([source.path, ref.offset, ref.operation, ref.name]);
          ownedNode(site, ref.name.name, 'SqlReference', ref.offset, { resolutionStatus: 'unresolved', reason: reason ?? 'unknown', operation: ref.operation });
          edge(fileId, site, 'Contains', ref.offset);
          diagnostics.push(`Unresolved ${ref.operation} reference ${ref.name.name}: ${reason ?? 'unknown'}.`);
          continue;
        }
        sharedNode(resolved.id, resolved.name, resolved.kind!, { resolutionStatus: resolved.kind === 'DatabaseRelation' ? 'external_qualified' : 'repository_declaration' });
        let from = fileId;
        if (ref.owner) {
          const owner = resolve(source.scope, ref.owner.name);
          if (!owner.id) {
            diagnostics.push(`Unresolved dependency owner ${ref.owner.name.name}.`);
            if (ref.owner.kind !== 'DatabaseView') continue;
          } else {
            from = owner.id;
            sharedNode(from, owner.name, owner.kind!);
          }
        }
        if (other?.id) {
          sharedNode(other.id, other.name, other.kind!);
          edge(resolved.id, other.id, 'JoinsWith', ref.offset, ref.properties);
        } else if (ref.properties?.sourceColumn && ref.properties.targetColumn) {
          const ownerName = effectiveName(ref.owner!.name);
          const fromColumn = { schema: ownerName.schema, name: ref.properties.sourceColumn };
          const toColumn = { schema: resolved.name.schema, name: ref.properties.targetColumn };
          const fromId = objectId(source.scope, fromColumn, 'column', nameParts(ownerName));
          const toId = objectId(source.scope, toColumn, 'column', nameParts(resolved.name));
          sharedNode(fromId, fromColumn, 'DatabaseColumn', { parentObject: from });
          sharedNode(toId, toColumn, 'DatabaseColumn', { parentObject: resolved.id });
          nodes.get(fromId)!.name = `${nodes.get(from)!.name}.${fromColumn.name}`;
          nodes.get(toId)!.name = `${nodes.get(resolved.id)!.name}.${toColumn.name}`;
          edge(fromId, toId, 'References', ref.offset);
        } else {
          edge(from, resolved.id, ref.operation, ref.offset, ref.properties);
          if (ref.owner?.kind === 'DatabaseView' && ref.operation === 'Reads' && from !== fileId) {
            edge(from, resolved.id, 'DependsOn', ref.offset);
            edge(fileId, resolved.id, 'Reads', ref.offset);
          }
        }
      }
    }
    const boundedDiagnostics = [...new Set(diagnostics)].slice(0, 100).map(d => d.slice(0, 2048));
    return { path: source.path, status: facts.status === 'failed' ? 'failed' : boundedDiagnostics.length ? 'partial' : 'complete',
      diagnostics: boundedDiagnostics, nodes: [...nodes.values()], edges };
  });
  return { contractVersion: '1', projectContext: project, files };
}
