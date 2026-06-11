import fs from 'node:fs';
import path from 'node:path';
import { Command } from 'commander';
import type { IndexCommandOptions, ResolvedIndexCommandOptions } from './options.js';
import { loadEnvironmentForInvocation } from '../config/load-environment.js';
import { resolveProjectName } from '../services/project-discovery.js';

export async function parseCommandLine(argv: string[]): Promise<ResolvedIndexCommandOptions> {
  const program = new Command()
    .name('codemeridian-ts-indexer')
    .description('TypeScript codebase indexer for CodeMeridian.')
    .argument('<path>', 'Root directory of the TypeScript project to index')
    .option('--project <name>', 'Short identifier for this project in the graph.')
    .option('--url <url>', 'CodeMeridian server URL.')
    .option('--clear', 'Wipe this project\'s existing data before indexing.')
    .option('--no-docs', 'Skip documentation ingestion.')
    .option('--watch', 'Stay running and re-index when .ts, .tsx or .md files change.')
    .option('--files-list <path>', 'Index only files listed in a newline-delimited text file.')
    .showHelpAfterError();

  program.parse(argv);

  const parsed = program.opts<{
    project?: string;
    url?: string;
    clear?: boolean;
    docs?: boolean;
    watch?: boolean;
    filesList?: string;
  }>();
  const [targetPath] = program.processedArgs as [string];

  const options: IndexCommandOptions = {
    path: targetPath,
    project: parsed.project,
    url: parsed.url,
    clear: parsed.clear ?? false,
    includeDocs: parsed.docs ?? true,
    watch: parsed.watch ?? false,
    filesList: parsed.filesList,
  };

  return resolveOptions(options);
}

function resolveOptions(options: IndexCommandOptions): ResolvedIndexCommandOptions {
  const rootPath = path.resolve(options.path);
  if (!fs.existsSync(rootPath)) {
    throw new Error(`directory not found: ${rootPath}`);
  }

  loadEnvironmentForInvocation(rootPath);

  const projectName = normalizeOptionalString(options.project)
    ?? normalizeOptionalString(process.env.CodeMeridian_Project)
    ?? resolveProjectName(rootPath);
  const serverUrl = normalizeOptionalString(options.url)
    ?? normalizeOptionalString(process.env.CodeMeridian_Url)
    ?? 'http://localhost:5100';

  return {
    rootPath,
    projectName,
    serverUrl,
    apiKey: normalizeOptionalString(process.env.CodeMeridian_Auth_ApiKey),
    clear: options.clear,
    includeDocs: options.includeDocs,
    watch: options.watch,
    filesListPath: options.filesList ? path.resolve(options.filesList) : undefined,
  };
}

function normalizeOptionalString(value?: string): string | undefined {
  return value && value.trim().length > 0 ? value.trim() : undefined;
}
