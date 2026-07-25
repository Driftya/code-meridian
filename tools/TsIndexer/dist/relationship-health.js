export class RelationshipOutcomeCollector {
    edgeKind;
    sampleLimitPerReason;
    attempted = 0;
    resolvedLocal = 0;
    externalOrUnindexed = 0;
    unresolvedLocal = 0;
    indeterminate = 0;
    duplicateEdges = 0;
    emittedEdgeKeys = new Set();
    reasons = new Map();
    samples = [];
    constructor(edgeKind, sampleLimitPerReason = 3) {
        this.edgeKind = edgeKind;
        this.sampleLimitPerReason = sampleLimitPerReason;
    }
    recordResolved(emittedEdgeKey) {
        this.attempted++;
        this.resolvedLocal++;
        if (emittedEdgeKey && this.emittedEdgeKeys.has(emittedEdgeKey)) {
            this.duplicateEdges++;
            return false;
        }
        else if (emittedEdgeKey) {
            this.emittedEdgeKeys.add(emittedEdgeKey);
        }
        return true;
    }
    record(disposition, reason, sample) {
        this.attempted++;
        if (disposition === 'external_or_unindexed')
            this.externalOrUnindexed++;
        if (disposition === 'unresolved_local')
            this.unresolvedLocal++;
        if (disposition === 'indeterminate')
            this.indeterminate++;
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
    build() {
        const result = {
            attempted: this.attempted,
            resolvedLocal: this.resolvedLocal,
            externalOrUnindexed: this.externalOrUnindexed,
            unresolvedLocal: this.unresolvedLocal,
            indeterminate: this.indeterminate,
            duplicateEdges: this.duplicateEdges,
            syntheticEdges: 0,
            uniqueResolvedEdges: this.emittedEdgeKeys.size,
            reasons: Object.fromEntries([...this.reasons.entries()].sort(([left], [right]) => left.localeCompare(right))),
            samples: [...this.samples].sort((left, right) => left.disposition.localeCompare(right.disposition)
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
export function relationshipEdgeKey(sourceId, targetId, edgeKind) {
    return `${sourceId}|${targetId}|${edgeKind}`;
}
