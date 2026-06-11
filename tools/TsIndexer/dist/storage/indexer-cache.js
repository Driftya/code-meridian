import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
const CACHE_VERSION = 1;
export function loadTsIndexerCache(cacheDirectory, projectName) {
    const filePath = getCacheFilePath(cacheDirectory, projectName);
    if (!fs.existsSync(filePath)) {
        return undefined;
    }
    try {
        const state = JSON.parse(fs.readFileSync(filePath, 'utf8'));
        return state.version === CACHE_VERSION ? state : undefined;
    }
    catch {
        return undefined;
    }
}
export function buildTsIndexerPlan(rootPath, files, existingState) {
    const current = files
        .map(filePath => createEntry(rootPath, filePath))
        .sort((a, b) => a.path.localeCompare(b.path));
    if (!existingState || existingState.files.length === 0) {
        return {
            changedFiles: current.map(entry => entry.path),
            deletedFiles: [],
            nextState: current,
            hasChanges: current.length > 0,
        };
    }
    const previous = new Map(existingState.files.map(entry => [entry.path, entry]));
    const currentByPath = new Set(current.map(entry => entry.path));
    const changedFiles = current
        .filter(entry => {
        const previousEntry = previous.get(entry.path);
        return !previousEntry || previousEntry.contentHash !== entry.contentHash;
    })
        .map(entry => entry.path);
    const deletedFiles = Array.from(previous.keys())
        .filter(pathValue => !currentByPath.has(pathValue))
        .sort((a, b) => a.localeCompare(b));
    return {
        changedFiles,
        deletedFiles,
        nextState: current,
        hasChanges: changedFiles.length > 0 || deletedFiles.length > 0,
    };
}
export function saveTsIndexerCache(cacheDirectory, projectName, nextState) {
    fs.mkdirSync(cacheDirectory, { recursive: true });
    const filePath = getCacheFilePath(cacheDirectory, projectName);
    const state = { version: CACHE_VERSION, files: nextState };
    fs.writeFileSync(filePath, `${JSON.stringify(state, null, 2)}\n`);
}
export function getCacheFilePath(cacheDirectory, projectName) {
    return path.join(cacheDirectory, `ts-indexer-files-${hash(projectName)}.json`);
}
function createEntry(rootPath, filePath) {
    const stats = fs.statSync(filePath);
    return {
        path: path.relative(rootPath, filePath).replace(/\\/g, '/'),
        lastWriteUtcTicks: stats.mtimeMs,
        length: stats.size,
        contentHash: hash(fs.readFileSync(filePath)),
    };
}
function hash(value) {
    return crypto.createHash('sha256').update(value).digest('hex').slice(0, 12);
}
