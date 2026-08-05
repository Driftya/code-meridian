export type RelationshipDisposition =
  | 'resolved_local'
  | 'external_or_unindexed'
  | 'unresolved_local'
  | 'indeterminate';

export interface RelationshipResolutionSample {
  edgeKind: 'Calls' | 'TypeReferences';
  disposition: Exclude<RelationshipDisposition, 'resolved_local'>;
  reason: string;
  sourceId: string;
  filePath?: string;
  fileRole?: string;
  lineNumber?: number;
  targetName?: string;
  receiverShape?: string;
}

export interface RelationshipOutcomeStats {
  attempted: number;
  resolvedLocal: number;
  externalOrUnindexed: number;
  unresolvedLocal: number;
  indeterminate: number;
  duplicateEdges: number;
  syntheticEdges: number;
  uniqueResolvedEdges: number;
  reasons: Record<string, number>;
  failureCountsByFileRole: Record<string, number>;
  samples: RelationshipResolutionSample[];
}

export interface TypeScriptRelationshipHealth {
  schemaVersion: 2;
  calls: RelationshipOutcomeStats;
  typeReferences: RelationshipOutcomeStats;
}

export class RelationshipOutcomeCollector {
  private attempted = 0;
  private resolvedLocal = 0;
  private externalOrUnindexed = 0;
  private unresolvedLocal = 0;
  private indeterminate = 0;
  private duplicateEdges = 0;
  private readonly emittedEdgeKeys = new Set<string>();
  private readonly reasons = new Map<string, number>();
  private readonly failureCountsByFileRole = new Map<string, number>();
  private readonly samples: RelationshipResolutionSample[] = [];

  constructor(
    private readonly edgeKind: 'Calls' | 'TypeReferences',
    private readonly sampleLimitPerReason = 3,
  ) {}

  recordResolved(emittedEdgeKey?: string): boolean {
    this.attempted++;
    this.resolvedLocal++;
    if (emittedEdgeKey && this.emittedEdgeKeys.has(emittedEdgeKey)) {
      this.duplicateEdges++;
      return false;
    } else if (emittedEdgeKey) {
      this.emittedEdgeKeys.add(emittedEdgeKey);
    }

    return true;
  }

  record(
    disposition: Exclude<RelationshipDisposition, 'resolved_local'>,
    reason: string,
    sample: Omit<RelationshipResolutionSample, 'edgeKind' | 'disposition' | 'reason'>,
  ): void {
    this.attempted++;
    if (disposition === 'external_or_unindexed') this.externalOrUnindexed++;
    if (disposition === 'unresolved_local') this.unresolvedLocal++;
    if (disposition === 'indeterminate') this.indeterminate++;

    const reasonKey = `${disposition}:${reason}`;
    this.reasons.set(reasonKey, (this.reasons.get(reasonKey) ?? 0) + 1);

    if (disposition !== 'external_or_unindexed') {
      const roleKey = `${disposition}:${sample.fileRole ?? 'Unknown'}`;
      this.failureCountsByFileRole.set(roleKey, (this.failureCountsByFileRole.get(roleKey) ?? 0) + 1);
      const candidate: RelationshipResolutionSample = {
        edgeKind: this.edgeKind,
        disposition,
        reason,
        ...sample,
      };
      const existingIndex = this.samples.findIndex(item =>
        item.disposition === disposition
        && item.reason === reason
        && item.fileRole === candidate.fileRole
        && item.receiverShape === candidate.receiverShape);
      if (existingIndex < 0) {
        this.samples.push(candidate);
      } else if (compareSamples(candidate, this.samples[existingIndex]!) < 0) {
        this.samples[existingIndex] = candidate;
      }
    }
  }

  build(): RelationshipOutcomeStats {
    const result: RelationshipOutcomeStats = {
      attempted: this.attempted,
      resolvedLocal: this.resolvedLocal,
      externalOrUnindexed: this.externalOrUnindexed,
      unresolvedLocal: this.unresolvedLocal,
      indeterminate: this.indeterminate,
      duplicateEdges: this.duplicateEdges,
      syntheticEdges: 0,
      uniqueResolvedEdges: this.emittedEdgeKeys.size,
      reasons: Object.fromEntries([...this.reasons.entries()].sort(([left], [right]) => left.localeCompare(right))),
      failureCountsByFileRole: Object.fromEntries(
        [...this.failureCountsByFileRole.entries()].sort(([left], [right]) => left.localeCompare(right)),
      ),
      samples: selectDiverseSamples(this.samples, this.sampleLimitPerReason),
    };

    if (result.attempted !== result.resolvedLocal
      + result.externalOrUnindexed
      + result.unresolvedLocal
      + result.indeterminate) {
      throw new Error(`Invalid ${this.edgeKind} relationship accounting.`);
    }

    return result;
  }
}

function selectDiverseSamples(
  candidates: RelationshipResolutionSample[],
  limitPerReason: number,
): RelationshipResolutionSample[] {
  const groups = new Map<string, RelationshipResolutionSample[]>();
  for (const candidate of candidates) {
    const key = `${candidate.disposition}|${candidate.reason}`;
    const group = groups.get(key) ?? [];
    group.push(candidate);
    groups.set(key, group);
  }

  return [...groups.entries()]
    .sort(([left], [right]) => left.localeCompare(right))
    .flatMap(([, group]) => selectGroupSamples(group, limitPerReason));
}

function selectGroupSamples(
  candidates: RelationshipResolutionSample[],
  limit: number,
): RelationshipResolutionSample[] {
  const ordered = [...candidates].sort(compareSamples);
  const selected: RelationshipResolutionSample[] = [];
  addFirstBy(ordered, selected, sample => sample.fileRole ?? 'Unknown', limit, fileRolePriority);
  addFirstBy(ordered, selected, sample => sample.receiverShape ?? 'Unknown', limit);
  selected.push(...ordered.filter(candidate => !selected.includes(candidate)).slice(0, limit - selected.length));
  return selected;
}

function addFirstBy(
  ordered: RelationshipResolutionSample[],
  selected: RelationshipResolutionSample[],
  keySelector: (sample: RelationshipResolutionSample) => string,
  limit: number,
  priority: (key: string) => number = () => 0,
): void {
  const firstByKey = new Map<string, RelationshipResolutionSample>();
  for (const candidate of ordered) {
    const key = keySelector(candidate);
    if (!firstByKey.has(key)) firstByKey.set(key, candidate);
  }

  for (const [key, candidate] of [...firstByKey.entries()]
    .sort(([left], [right]) => priority(left) - priority(right) || left.localeCompare(right))) {
    if (selected.length === limit) return;
    if (!selected.includes(candidate)) selected.push(candidate);
  }
}

function compareSamples(left: RelationshipResolutionSample, right: RelationshipResolutionSample): number {
  return (left.filePath ?? '').localeCompare(right.filePath ?? '')
    || (left.lineNumber ?? 0) - (right.lineNumber ?? 0)
    || left.sourceId.localeCompare(right.sourceId)
    || (left.targetName ?? '').localeCompare(right.targetName ?? '');
}

function fileRolePriority(fileRole: string): number {
  if (fileRole === 'Source') return 0;
  if (fileRole === 'Test') return 1;
  return 2;
}

export function relationshipEdgeKey(sourceId: string, targetId: string, edgeKind: string): string {
  return `${sourceId}|${targetId}|${edgeKind}`;
}
