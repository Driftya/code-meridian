import fs from 'node:fs';
import path from 'node:path';
const ignoredDirectoryNames = new Set([
    '.git',
    '.vs',
    '.vscode',
    '.meridian',
    'bin',
    'obj',
    'node_modules',
    'dist',
    'build',
    'coverage',
]);
export function readFilesList(filePath) {
    return fs
        .readFileSync(path.resolve(filePath), 'utf8')
        .split(/\r?\n/)
        .map(line => line.trim())
        .filter(line => line.length > 0);
}
export function resolveDocumentFiles(rootPath, files) {
    if (files) {
        return files
            .map(file => resolveInputFile(rootPath, file))
            .filter(file => isDocumentationFile(file) && !isIgnoredPath(rootPath, file) && fs.existsSync(file));
    }
    return enumerateFiles(rootPath)
        .filter(file => isDocumentationFile(file) && !isIgnoredPath(rootPath, file));
}
export function enumerateFiles(rootPath) {
    const results = [];
    const pending = [rootPath];
    while (pending.length > 0) {
        const current = pending.pop();
        if (!current || isIgnoredPath(rootPath, current))
            continue;
        for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
            const fullPath = path.join(current, entry.name);
            if (entry.isDirectory()) {
                pending.push(fullPath);
            }
            else if (entry.isFile()) {
                results.push(fullPath);
            }
        }
    }
    return results;
}
export function containsFile(rootPath, ...extensions) {
    return enumerateFiles(rootPath).some(file => extensions.includes(path.extname(file).toLowerCase()) && isTypeScriptSourceFile(file));
}
export function discoverTypeScriptFiles(rootPath) {
    return enumerateFiles(rootPath).filter(isTypeScriptSourceFile);
}
export function findTypeScriptRoots(rootPath) {
    const directories = enumerateDirectories(rootPath);
    const roots = directories.filter(directory => fs.existsSync(path.join(directory, 'tsconfig.json')) && containsFile(directory, '.ts', '.tsx'));
    if (roots.length === 0 && containsFile(rootPath, '.ts', '.tsx')) {
        return [rootPath];
    }
    return roots.filter(candidate => !roots.some(other => other !== candidate && isSubdirectoryOf(candidate, other)));
}
export function resolveInputFile(rootPath, filePath) {
    return path.isAbsolute(filePath)
        ? path.resolve(filePath)
        : path.resolve(rootPath, filePath);
}
export function isTypeScriptSourceFile(filePath) {
    return (filePath.endsWith('.ts') || filePath.endsWith('.tsx')) && !filePath.endsWith('.d.ts');
}
export function isDocumentationFile(filePath) {
    const name = path.basename(filePath).toLowerCase();
    return name.endsWith('.md') ||
        name.endsWith('.txt') ||
        name === 'readme.md' ||
        name === 'architecture.md' ||
        name === 'changelog.md' ||
        name === 'agents.md';
}
export function buildTypeScriptSourceFileGlobs(rootPath) {
    const normalizedRoot = rootPath.replace(/\\/g, '/');
    return [
        path.join(normalizedRoot, '**/*.ts').replace(/\\/g, '/'),
        path.join(normalizedRoot, '**/*.tsx').replace(/\\/g, '/'),
        ...Array.from(ignoredDirectoryNames, directoryName => `!${path.join(normalizedRoot, `**/${directoryName}/**`).replace(/\\/g, '/')}`),
        `!${path.join(normalizedRoot, '**/*.d.ts').replace(/\\/g, '/')}`,
    ];
}
export function isIgnoredDirectoryName(name) {
    return ignoredDirectoryNames.has(name.toLowerCase());
}
export function isIgnoredPath(rootPath, filePath) {
    const relPath = path.relative(rootPath, filePath).replace(/\\/g, '/');
    const segments = relPath.split('/').filter(segment => segment.length > 0);
    return segments.some(isIgnoredDirectoryName);
}
export function resolveProjectName(dir) {
    const packageJsonPath = path.join(dir, 'package.json');
    if (fs.existsSync(packageJsonPath)) {
        try {
            const name = JSON.parse(fs.readFileSync(packageJsonPath, 'utf8')).name;
            if (name)
                return name;
        }
        catch {
            // Ignore malformed package.json and continue with fallbacks.
        }
    }
    const workspaces = fs.readdirSync(dir).filter(file => file.endsWith('.code-workspace'));
    if (workspaces.length > 0)
        return path.basename(workspaces[0], '.code-workspace');
    return path.basename(dir);
}
function enumerateDirectories(rootPath) {
    const results = [];
    const pending = [rootPath];
    while (pending.length > 0) {
        const current = pending.pop();
        if (!current || isIgnoredPath(rootPath, current))
            continue;
        results.push(current);
        for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
            if (!entry.isDirectory())
                continue;
            pending.push(path.join(current, entry.name));
        }
    }
    return results;
}
function isSubdirectoryOf(candidate, parent) {
    const relative = path.relative(parent, candidate);
    return relative !== '.' && !relative.startsWith('..') && !path.isAbsolute(relative);
}
