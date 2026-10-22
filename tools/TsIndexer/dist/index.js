import { parseCommandLine } from './cli/program.js';
import { TypeScriptIndexerApplication } from './application/type-script-indexer-application.js';
import { describeWorker, verifyWorkerIdentity } from '../../IndexerShared/dist/index.js';
import { workerIdentity } from './worker-identity.js';
try {
    const identity = workerIdentity();
    if (!describeWorker(identity)) {
        verifyWorkerIdentity(identity);
        const options = await parseCommandLine(process.argv);
        const application = new TypeScriptIndexerApplication();
        const exitCode = await application.run(options);
        process.exitCode = exitCode;
        verifyWorkerIdentity(workerIdentity(), JSON.stringify(identity.fingerprint));
    }
}
catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error(`error: ${message}`);
    process.exitCode = 1;
}
