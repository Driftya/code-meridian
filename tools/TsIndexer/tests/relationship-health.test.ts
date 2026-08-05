import { describe, expect, it } from 'vitest';
import { RelationshipOutcomeCollector } from '../src/relationship-health.js';

describe('RelationshipOutcomeCollector', () => {
  it('selects bounded deterministic samples across file roles and receiver shapes', () => {
    const candidates = [
      sample('tests/Zeta.test.ts', 'Test', 'PropertyAccessExpression', 40),
      sample('src/Zeta.ts', 'Source', 'PropertyAccessExpression', 30),
      sample('src/Alpha.ts', 'Source', 'CallExpression', 20),
      sample('generated/Mapper.ts', 'Generated', 'PropertyAccessExpression', 10),
    ];

    const forward = build(candidates);
    const reverse = build([...candidates].reverse());

    expect(forward).toEqual(reverse);
    expect(forward).toHaveLength(3);
    expect(forward.map(candidate => candidate.fileRole)).toEqual(['Source', 'Test', 'Generated']);
    expect(forward.every(candidate => candidate.reason === 'unknown_receiver_provenance')).toBe(true);
  });
});

function build(candidates: ReturnType<typeof sample>[]) {
  const collector = new RelationshipOutcomeCollector('Calls');
  for (const candidate of candidates) {
    collector.record('indeterminate', 'unknown_receiver_provenance', candidate);
  }
  return collector.build().samples;
}

function sample(filePath: string, fileRole: string, receiverShape: string, lineNumber: number) {
  return {
    sourceId: `Proj:Method:${filePath}:run`,
    filePath,
    fileRole,
    receiverShape,
    lineNumber,
    targetName: 'run',
  };
}
