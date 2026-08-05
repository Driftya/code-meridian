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
    failureCountsByFileRole = new Map();
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
        if (disposition !== 'external_or_unindexed') {
            const roleKey = `${disposition}:${sample.fileRole ?? 'Unknown'}`;
            this.failureCountsByFileRole.set(roleKey, (this.failureCountsByFileRole.get(roleKey) ?? 0) + 1);
            const candidate = {
                edgeKind: this.edgeKind,
                disposition,
                reason,
                ...sample,
            };
            const existingIndex = this.samples.findIndex(item => item.disposition === disposition
                && item.reason === reason
                && item.fileRole === candidate.fileRole
                && item.receiverShape === candidate.receiverShape);
            if (existingIndex < 0) {
                this.samples.push(candidate);
            }
            else if (compareSamples(candidate, this.samples[existingIndex]) < 0) {
                this.samples[existingIndex] = candidate;
            }
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
            failureCountsByFileRole: Object.fromEntries([...this.failureCountsByFileRole.entries()].sort(([left], [right]) => left.localeCompare(right))),
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
function selectDiverseSamples(candidates, limitPerReason) {
    const groups = new Map();
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
function selectGroupSamples(candidates, limit) {
    const ordered = [...candidates].sort(compareSamples);
    const selected = [];
    addFirstBy(ordered, selected, sample => sample.fileRole ?? 'Unknown', limit, fileRolePriority);
    addFirstBy(ordered, selected, sample => sample.receiverShape ?? 'Unknown', limit);
    selected.push(...ordered.filter(candidate => !selected.includes(candidate)).slice(0, limit - selected.length));
    return selected;
}
function addFirstBy(ordered, selected, keySelector, limit, priority = () => 0) {
    const firstByKey = new Map();
    for (const candidate of ordered) {
        const key = keySelector(candidate);
        if (!firstByKey.has(key))
            firstByKey.set(key, candidate);
    }
    for (const [key, candidate] of [...firstByKey.entries()]
        .sort(([left], [right]) => priority(left) - priority(right) || left.localeCompare(right))) {
        if (selected.length === limit)
            return;
        if (!selected.includes(candidate))
            selected.push(candidate);
    }
}
function compareSamples(left, right) {
    return (left.filePath ?? '').localeCompare(right.filePath ?? '')
        || (left.lineNumber ?? 0) - (right.lineNumber ?? 0)
        || left.sourceId.localeCompare(right.sourceId)
        || (left.targetName ?? '').localeCompare(right.targetName ?? '');
}
function fileRolePriority(fileRole) {
    if (fileRole === 'Source')
        return 0;
    if (fileRole === 'Test')
        return 1;
    return 2;
}
export function relationshipEdgeKey(sourceId, targetId, edgeKind) {
    return `${sourceId}|${targetId}|${edgeKind}`;
}
