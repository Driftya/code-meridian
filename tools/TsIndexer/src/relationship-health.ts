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
  lineNumber?: number;
  targetName?: string;
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

    if (disposition !== 'external_or_unindexed'
      && this.samples.filter(item => item.disposition === disposition && item.reason === reason).length < this.sampleLimitPerReason) {
      this.samples.push({
        edgeKind: this.edgeKind,
        disposition,
        reason,
        ...sample,
      });
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
      samples: [...this.samples].sort((left, right) =>
        left.disposition.localeCompare(right.disposition)
        || left.reason.localeCompare(right.reason)
        || left.sourceId.localeCompare(right.sourceId)
        || (left.targetName ?? '').localeCompare(right.targetName ?? '')),
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

export function relationshipEdgeKey(sourceId: string, targetId: string, edgeKind: string): string {
  return `${sourceId}|${targetId}|${edgeKind}`;
}
