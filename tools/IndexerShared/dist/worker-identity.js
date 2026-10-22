import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
/** Resolve dependencies from the worker (or its parser), never from the shared utility. */
export function createWorkerIdentity(analyzer, entryUrl, dependencies, parserRuntime = {}) {
    const workerDirectory = path.dirname(fileURLToPath(entryUrl));
    const runtime = { versions: process.versions, platform: process.platform, architecture: process.arch };
    const require = createRequire(entryUrl);
    const runtimeDependencies = [...dependencies];
    if (process.execArgv.some(argument => /[\\/]tsx[\\/]/.test(argument)))
        runtimeDependencies.push('tsx');
    const parsers = runtimeDependencies.map(dependency => {
        const name = typeof dependency === 'string' ? dependency : dependency.name;
        const resolver = typeof dependency === 'string' ? require : createRequire(require.resolve(dependency.from));
        const entry = resolver.resolve(name);
        let directory = path.dirname(entry);
        while (true) {
            const manifest = path.join(directory, 'package.json');
            if (fs.existsSync(manifest)) {
                const metadata = JSON.parse(fs.readFileSync(manifest, 'utf8'));
                if (metadata.name === name) {
                    if (!metadata.version)
                        throw new Error(`Missing runtime version for ${name}.`);
                    return { name, version: metadata.version, content: directoryHash(directory) };
                }
            }
            const parent = path.dirname(directory);
            if (parent === directory)
                throw new Error(`Missing runtime identity for ${name}.`);
            directory = parent;
        }
    });
    return {
        contractVersion: '1', analyzer, runtime, parsers, parserRuntime,
        fingerprint: {
            implementation: hash(JSON.stringify({ contract: '1', analyzer, runtime,
                worker: directoryHash(workerDirectory), shared: directoryHash(path.dirname(fileURLToPath(import.meta.url))),
                manifest: fs.readFileSync(path.join(workerDirectory, '..', 'package.json'), 'utf8'),
                configuration: fs.readFileSync(path.join(workerDirectory, '..', 'tsconfig.json'), 'utf8') })),
            parser: hash(JSON.stringify({ runtime, parsers, parserRuntime })),
        },
    };
}
/** Describe mode does not read repository settings, discover source files, or contact a server. */
export function describeWorker(identity) {
    if (process.argv.length !== 3 || process.argv[2] !== '--describe-worker')
        return false;
    process.stdout.write(JSON.stringify(identity) + '\n');
    return true;
}
export function verifyWorkerIdentity(identity, expected = process.env.CODEMERIDIAN_EXPECTED_WORKER_FINGERPRINT) {
    if (!expected || expected === 'unknown')
        return;
    const fingerprint = JSON.parse(expected);
    if (fingerprint.implementation !== identity.fingerprint.implementation || fingerprint.parser !== identity.fingerprint.parser)
        throw new Error('Worker identity changed after profile discovery; rerun indexing to publish the current identity.');
}
function directoryHash(directory) {
    const entries = [];
    function visit(current) {
        for (const entry of fs.readdirSync(current, { withFileTypes: true }).sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0)) {
            if (['node_modules', '.git', 'coverage', 'tests'].includes(entry.name))
                continue;
            const fullPath = path.join(current, entry.name);
            if (entry.isDirectory())
                visit(fullPath);
            else if (entry.isFile() && /\.(?:[cm]?js|ts|json|wasm)$/.test(entry.name) && !/\.d\.ts$/.test(entry.name))
                entries.push([path.relative(directory, fullPath).replace(/\\/g, '/'), hash(fs.readFileSync(fullPath))]);
        }
    }
    visit(directory);
    if (entries.length === 0)
        throw new Error('Worker or parser runtime files are missing.');
    return hash(JSON.stringify(entries));
}
function hash(value) {
    return 'sha256:' + createHash('sha256').update(value).digest('hex');
}
