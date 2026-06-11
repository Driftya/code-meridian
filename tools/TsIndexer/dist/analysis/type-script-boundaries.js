import fs from 'node:fs';
import path from 'node:path';
import { findTypeScriptRoots, resolveProjectName } from '../services/project-discovery.js';
export function analyzeTypeScriptBoundaries(rootPath) {
    const roots = findTypeScriptRoots(rootPath);
    return roots.map(root => {
        const projectName = resolveProjectName(root);
        return {
            rootPath: root,
            projectName,
            tsconfigPath: path.join(root, 'tsconfig.json'),
            hasEslintConfig: hasEslintConfig(root),
        };
    });
}
function hasEslintConfig(rootPath) {
    return [
        '.eslintrc',
        '.eslintrc.json',
        '.eslintrc.js',
        '.eslintrc.cjs',
        '.eslintrc.mjs',
        'eslint.config.js',
        'eslint.config.cjs',
        'eslint.config.mjs',
    ].some(file => fs.existsSync(path.join(rootPath, file)));
}
