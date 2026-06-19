import { parseCommandLine } from './cli/program.js';
import { HtmlCssIndexerApplication } from './application/html-css-indexer-application.js';

try {
  const options = await parseCommandLine(process.argv);
  const application = new HtmlCssIndexerApplication();
  const exitCode = await application.run(options);
  process.exit(exitCode);
} catch (error) {
  const message = error instanceof Error ? error.message : String(error);
  console.error(`error: ${message}`);
  process.exit(1);
}
