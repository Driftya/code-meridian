import { ts } from 'ts-morph';
import { createWorkerIdentity } from '../../IndexerShared/dist/index.js';

export function workerIdentity() {
  return createWorkerIdentity('frontend', import.meta.url,
    ['postcss', 'postcss-scss', 'ts-morph', { name: '@ts-morph/common', from: 'ts-morph' }, 'commander'],
    { typescript: ts.version });
}
