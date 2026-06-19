import path from 'node:path';
import { config as loadDotEnv } from 'dotenv';
import { findUpSync } from 'find-up';
export function loadEnvironmentForInvocation(rootPath, cwd = process.cwd()) {
    const currentEnvPath = findUpSync('.env', { cwd });
    const targetEnvPath = findUpSync('.env', { cwd: rootPath });
    if (currentEnvPath) {
        loadDotEnv({ path: currentEnvPath, override: false });
    }
    if (targetEnvPath && normalizePath(targetEnvPath) !== normalizePath(currentEnvPath)) {
        loadDotEnv({ path: targetEnvPath, override: true });
    }
}
function normalizePath(filePath) {
    return filePath ? path.resolve(filePath) : undefined;
}
