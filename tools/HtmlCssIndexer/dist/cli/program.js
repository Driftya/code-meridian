import path from 'node:path';
import { Command } from 'commander';
import { loadEnvironmentForInvocation } from '../../../IndexerShared/dist/index.js';
export async function parseCommandLine(argv) {
    const program = new Command()
        .name('codemeridian-html-css-indexer')
        .description('Internal HTML/CSS/SCSS worker for CodeMeridian.Indexer.')
        .argument('<path>', 'Root directory of the frontend project to index')
        .requiredOption('--project <name>', 'Project context name.')
        .requiredOption('--url <url>', 'CodeMeridian server URL.')
        .requiredOption('--batch-file <path>', 'JSON batch file written by CodeMeridian.Indexer.')
        .showHelpAfterError();
    program.parse(argv);
    const parsed = program.opts();
    const [targetPath] = program.processedArgs;
    const options = {
        path: targetPath,
        project: parsed.project,
        url: parsed.url,
        batchFile: parsed.batchFile,
    };
    return resolveOptions(options);
}
function resolveOptions(options) {
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
function normalizeOptionalString(value) {
    return value && value.trim().length > 0 ? value.trim() : undefined;
}
function normalizeRequiredString(value, name) {
    const normalized = normalizeOptionalString(value);
    if (!normalized) {
        throw new Error(`${name} is required`);
    }
    return normalized;
}
