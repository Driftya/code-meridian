import fs from 'node:fs';
import path from 'node:path';
import { findUpSync } from 'find-up';
export function loadMeridianConfigForInvocation(rootPath) {
    const localPath = findUpSync('meridian.json', { cwd: rootPath });
    const globalPath = resolveGlobalConfigPath();
    return {
        local: loadMeridianConfig(localPath),
        global: loadMeridianConfig(globalPath),
    };
}
export function resolveGlobalConfigDirectory() {
    const overridePath = process.env.CODEMERIDIAN_CONFIG_HOME;
    if (overridePath?.trim()) {
        return path.resolve(overridePath.trim());
    }
    if (process.platform === 'win32') {
        const localAppData = process.env.LOCALAPPDATA;
        if (localAppData?.trim()) {
            return path.join(localAppData.trim(), 'CodeMeridian');
        }
    }
    const home = process.env.HOME ?? process.env.USERPROFILE;
    if (home?.trim()) {
        return path.join(home.trim(), '.codemeridian');
    }
    return path.resolve('.codemeridian');
}
export function resolveGlobalConfigPath() {
    return path.join(resolveGlobalConfigDirectory(), 'meridian.json');
}
function loadMeridianConfig(filePath) {
    if (!filePath || !fs.existsSync(filePath)) {
        return undefined;
    }
    try {
        return JSON.parse(fs.readFileSync(filePath, 'utf8'));
    }
    catch {
        return undefined;
    }
}
