import path from 'node:path';
import { Command } from 'commander';
import { loadEnvironmentForInvocation } from '#indexer-shared';
import type { IndexCommandOptions, ResolvedIndexCommandOptions } from '#indexer-shared';

export async function parseCommandLine(argv: string[]): Promise<ResolvedIndexCommandOptions> {
  const program = new Command()
    .name('codemeridian-html-css-indexer')
    .description('Internal HTML/CSS/SCSS worker for CodeMeridian.Indexer.')
    .argument('<path>', 'Root directory of the frontend project to index')
    .requiredOption('--project <name>', 'Project context name.')
    .requiredOption('--url <url>', 'CodeMeridian server URL.')
    .requiredOption('--batch-file <path>', 'JSON batch file written by CodeMeridian.Indexer.')
    .showHelpAfterError();

  program.parse(argv);

  const parsed = program.opts<{
    project: string;
    url: string;
    batchFile: string;
  }>();
  const [targetPath] = program.processedArgs as [string];

  const options: IndexCommandOptions = {
    path: targetPath,
    project: parsed.project,
    url: parsed.url,
    batchFile: parsed.batchFile,
  };

  return resolveOptions(options);
}

function resolveOptions(options: IndexCommandOptions): ResolvedIndexCommandOptions {
  const rootPath = path.resolve(options.path);

  loadEnvironmentForInvocation(rootPath);

  return {
    rootPath,
    projectName: normalizeRequiredString(options.project, 'project'),
    serverUrl: normalizeRequiredString(options.url, 'url'),
    apiKey: normalizeOptionalString(process.env.CodeMeridian_Auth_ApiKey),
    batchFilePath: path.resolve(options.batchFile),
  };
}

function normalizeOptionalString(value?: string): string | undefined {
  return value && value.trim().length > 0 ? value.trim() : undefined;
}

function normalizeRequiredString(value: string, name: string): string {
  const normalized = normalizeOptionalString(value);
  if (!normalized) {
    throw new Error(`${name} is required`);
  }

  return normalized;
}
