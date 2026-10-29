import { parseCommandLine } from './cli/program.js';
import { HtmlCssIndexerApplication } from './application/html-css-indexer-application.js';
import { describeWorker, verifyWorkerIdentity, ProducerReport } from '../../IndexerShared/dist/index.js';
import { workerIdentity } from './worker-identity.js';

try {
  const identity = workerIdentity();
  if (!describeWorker(identity)) {
    verifyWorkerIdentity(identity);
    const options = await parseCommandLine(process.argv);
    const application = new HtmlCssIndexerApplication();
    const report = new ProducerReport('frontend', options.projectName, options.rootPath, identity.fingerprint);
    const exitCode = await application.run(options, report);
    process.exitCode = exitCode;
    verifyWorkerIdentity(workerIdentity(), JSON.stringify(identity.fingerprint));
    report.write();
  }
} catch (error) {
  const message = error instanceof Error ? error.message : String(error);
  console.error(`error: ${message}`);
  process.exitCode = 1;
}
