import fs from 'node:fs';
import crypto from 'node:crypto';
import path from 'node:path';
import { Command } from 'commander';
import { loadEnvironmentForInvocation } from '../config/load-environment.js';
import { loadMeridianConfigForInvocation } from '../config/meridian-config.js';
import { resolveProjectName } from '../services/project-discovery.js';
import { resolveGlobalConfigDirectory } from '../config/meridian-config.js';
export async function parseCommandLine(argv) {
    const program = new Command()
        .name('codemeridian-ts-indexer')
        .description('TypeScript codebase indexer for CodeMeridian.')
        .argument('<path>', 'Root directory of the TypeScript project to index')
        .option('--project <name>', 'Short identifier for this project in the graph.')
        .option('--url <url>', 'CodeMeridian server URL.')
        .option('--clear', 'Wipe this project\'s existing data before indexing.')
        .option('--full', 'Force a full TypeScript reindex without using the incremental cache.')
        .option('--no-docs', 'Skip documentation ingestion.')
        .option('--watch', 'Stay running and re-index when .ts, .tsx or .md files change.')
        .option('--files-list <path>', 'Index only files listed in a newline-delimited text file.')
        .showHelpAfterError();
    program.parse(argv);
    const parsed = program.opts();
    const [targetPath] = program.processedArgs;
    const options = {
        path: targetPath,
        project: parsed.project,
        url: parsed.url,
        clear: parsed.clear ?? false,
        forceFull: parsed.full ?? false,
        includeDocs: parsed.docs ?? true,
        watch: parsed.watch ?? false,
        filesList: parsed.filesList,
    };
    return resolveOptions(options);
}
function resolveOptions(options) {
    const rootPath = path.resolve(options.path);
    if (!fs.existsSync(rootPath)) {
        throw new Error(`directory not found: ${rootPath}`);
    }
    loadEnvironmentForInvocation(rootPath);
    const meridianConfig = loadMeridianConfigForInvocation(rootPath);
    const repositoryRoot = findRepositoryRoot(rootPath) ?? rootPath;
    const projectName = normalizeOptionalString(options.project)
        ?? normalizeOptionalString(process.env.CodeMeridian_Project)
        ?? normalizeOptionalString(meridianConfig.local?.project)
        ?? normalizeOptionalString(meridianConfig.global?.project)
        ?? resolveProjectName(rootPath);
    const serverUrl = normalizeOptionalString(options.url)
        ?? normalizeOptionalString(process.env.CodeMeridian_Url)
        ?? normalizeOptionalString(meridianConfig.local?.codeMeridianUrl ?? meridianConfig.local?.url)
        ?? normalizeOptionalString(meridianConfig.global?.codeMeridianUrl ?? meridianConfig.global?.url)
        ?? 'http://localhost:5100';
    const useGlobalCache = meridianConfig.local?.useGlobalCache ?? meridianConfig.global?.useGlobalCache ?? false;
    const cacheDirectory = useGlobalCache
        ? path.join(resolveGlobalConfigDirectory(), 'projects', resolveCacheProjectKey(rootPath, projectName), 'cache')
        : path.join(repositoryRoot, '.meridian', 'cache');
    return {
        rootPath,
        projectName,
        serverUrl,
        apiKey: normalizeOptionalString(process.env.CodeMeridian_Auth_ApiKey),
        clear: options.clear,
        forceFull: options.forceFull,
        includeDocs: options.includeDocs,
        watch: options.watch,
        filesListPath: options.filesList ? path.resolve(options.filesList) : undefined,
        storageMode: useGlobalCache ? 'global' : 'repo',
        cacheDirectory,
    };
}
function normalizeOptionalString(value) {
    return value && value.trim().length > 0 ? value.trim() : undefined;
}
function resolveCacheProjectKey(rootPath, projectName) {
    const normalized = projectName.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
    const hash = crypto.createHash('sha256').update(rootPath.replace(/\\/g, '/').toLowerCase()).digest('hex').slice(0, 12);
    return `${normalized || 'project'}-${hash}`;
}
function findRepositoryRoot(startPath) {
    for (let current = path.resolve(startPath);; current = path.dirname(current)) {
        if (fs.existsSync(path.join(current, '.git')) ||
            fs.existsSync(path.join(current, 'CodeMeridian.sln')) ||
            fs.existsSync(path.join(current, 'CodeMeridian.slnx'))) {
            return current;
        }
        const parent = path.dirname(current);
        if (parent === current) {
            return undefined;
        }
    }
}
