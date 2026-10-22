import { ts } from 'ts-morph';
import { createWorkerIdentity } from '../../IndexerShared/dist/index.js';
export function workerIdentity() {
    return createWorkerIdentity('typescript', import.meta.url, ['ts-morph', { name: '@ts-morph/common', from: 'ts-morph' }, 'commander'], { typescript: ts.version });
}
