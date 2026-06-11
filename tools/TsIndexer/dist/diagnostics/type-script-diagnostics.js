import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import crypto from 'node:crypto';
export async function indexTypeScriptDiagnostics(client, rootPath, projectName) {
    const roots = findTypeScriptRoots(rootPath);
    const findings = [];
    for (const typeScriptRoot of roots) {
        const tsc = resolveLocalNodeBinary(typeScriptRoot, 'tsc');
        if (!tsc) {
            console.log(`  TypeScript diagnostics unavailable in ${path.relative(rootPath, typeScriptRoot)}: local tsc not found.`);
            continue;
        }
        const result = await runCaptureAsync(tsc, ['--noEmit', '--pretty', 'false', '--noUnusedLocals', '--noUnusedParameters'], typeScriptRoot);
        const parsed = parseTypeScriptDiagnostics(result.output, rootPath, typeScriptRoot, projectName);
        findings.push(...parsed);
        console.log(`  tsc ${path.relative(rootPath, typeScriptRoot)} exit code ${result.exitCode}; parsed ${parsed.length} diagnostics.`);
        const lintCommand = resolveLintCommand(typeScriptRoot);
        if (lintCommand) {
            const lintResult = await runCaptureAsync(lintCommand.fileName, lintCommand.arguments, typeScriptRoot);
            const lintParsed = parseLintDiagnostics(lintResult.output, rootPath, typeScriptRoot, projectName);
            findings.push(...lintParsed);
            console.log(`  lint ${path.relative(rootPath, typeScriptRoot)} exit code ${lintResult.exitCode}; parsed ${lintParsed.length} diagnostics.`);
        }
    }
    const distinct = findings
        .filter(finding => finding.filePath.length > 0)
        .filter((finding, index, items) => items.findIndex(other => other.id === finding.id) === index);
    for (const finding of distinct) {
        await client.ingestNode({
            id: finding.id,
            name: `${finding.severity} ${finding.code}`,
            type: 'Diagnostic',
            namespace: finding.source,
            filePath: finding.filePath,
            lineNumber: finding.line,
            summary: finding.message,
            projectContext: projectName,
        });
        await client.ingestEdge({
            sourceId: `${projectName}::File::${finding.filePath}`,
            targetId: finding.id,
            type: 'Contains',
        });
    }
    return distinct.length;
}
export function parseTypeScriptDiagnostics(output, rootPath, workingDirectory, project) {
    const findings = [];
    const pattern = /^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\):\s(?<severity>error|warning)\s(?<code>TS\d+):\s(?<message>.+)$/i;
    for (const rawLine of output.split(/[\r\n]+/)) {
        const match = pattern.exec(rawLine.trimEnd());
        if (!match?.groups)
            continue;
        findings.push(createDiagnostic(project, 'tsc', match.groups.severity, match.groups.code, match.groups.message.trim(), normalizePath(match.groups.file, rootPath, workingDirectory), Number.parseInt(match.groups.line, 10), Number.parseInt(match.groups.column, 10)));
    }
    return findings;
}
export function parseLintDiagnostics(output, rootPath, workingDirectory, project) {
    const findings = [];
    const pattern = /^\s*(?<line>\d+):(?<column>\d+)\s+(?<severity>error|warning|warn)\s+(?<message>.+?)\s+(?<code>[@\w/-]+)$/i;
    let currentFile;
    for (const rawLine of output.split(/[\r\n]+/)) {
        const line = rawLine.trimEnd();
        if (line.length === 0)
            continue;
        if (!line.startsWith(' ') && looksLikePath(line)) {
            currentFile = normalizePath(line, rootPath, workingDirectory);
            continue;
        }
        if (!currentFile)
            continue;
        const match = pattern.exec(line);
        if (!match?.groups)
            continue;
        findings.push(createDiagnostic(project, 'eslint', match.groups.severity, match.groups.code, match.groups.message.trim(), currentFile, Number.parseInt(match.groups.line, 10), Number.parseInt(match.groups.column, 10)));
    }
    return findings;
}
async function runCaptureAsync(fileName, arguments_, workingDirectory) {
    const child = spawn(fileName, arguments_, {
        cwd: workingDirectory,
        shell: false,
        windowsHide: true,
    });
    let stdout = '';
    let stderr = '';
    child.stdout?.on('data', chunk => {
        stdout += chunk.toString();
    });
    child.stderr?.on('data', chunk => {
        stderr += chunk.toString();
    });
    const exitCode = await new Promise(resolve => {
        child.on('close', code => resolve(code ?? 0));
    });
    return { exitCode, output: `${stdout}\n${stderr}`.trim() };
}
function createDiagnostic(project, source, severity, code, message, filePath, line, column) {
    const normalizedSeverity = severity.toLowerCase() === 'warn' ? 'warning' : severity.toLowerCase();
    const hashInput = `${project}|${source}|${normalizedSeverity}|${code}|${filePath}|${line}|${column}|${message}`;
    return {
        id: `${project}::Diagnostic::${hash(hashInput)}`,
        severity: normalizedSeverity,
        code,
        message,
        filePath,
        line,
        column,
        source,
    };
}
function normalizePath(filePath, rootPath, workingDirectory) {
    const trimmed = filePath.trim().replace(/^"|"$/g, '');
    const fullPath = path.isAbsolute(trimmed) ? trimmed : path.resolve(workingDirectory, trimmed);
    return path.relative(rootPath, fullPath).replace(/\\/g, '/');
}
function resolveLocalNodeBinary(rootPath, name) {
    const executable = process.platform === 'win32' ? `${name}.cmd` : name;
    for (let current = rootPath;; current = path.dirname(current)) {
        const candidate = path.join(current, 'node_modules', '.bin', executable);
        if (fs.existsSync(candidate))
            return candidate;
        const parent = path.dirname(current);
        if (parent === current)
            return undefined;
    }
}
function resolveLintCommand(rootPath) {
    const packageJson = path.join(rootPath, 'package.json');
    if (fs.existsSync(packageJson)) {
        try {
            const content = JSON.parse(fs.readFileSync(packageJson, 'utf8'));
            if (content.scripts?.lint) {
                return { fileName: npmCommand(), arguments: ['run', 'lint'] };
            }
        }
        catch {
            // Ignore malformed package.json and fall back to local eslint.
        }
    }
    const eslint = resolveLocalNodeBinary(rootPath, 'eslint');
    return eslint ? { fileName: eslint, arguments: ['.'] } : undefined;
}
function findTypeScriptRoots(rootPath) {
    const directories = enumerateDirectories(rootPath);
    const roots = directories.filter(directory => fs.existsSync(path.join(directory, 'tsconfig.json')) && containsTypeScriptFile(directory));
    if (roots.length === 0 && containsTypeScriptFile(rootPath)) {
        return [rootPath];
    }
    return roots.filter(candidate => !roots.some(other => other !== candidate && isSubdirectoryOf(candidate, other)));
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
            if (entry.isDirectory()) {
                pending.push(path.join(current, entry.name));
            }
        }
    }
    return results;
}
function containsTypeScriptFile(rootPath) {
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
            else if (entry.isFile() && isTypeScriptSourceFile(fullPath)) {
                return true;
            }
        }
    }
    return false;
}
function looksLikePath(value) {
    return value.includes('/') || value.includes('\\') || value.endsWith('.ts') || value.endsWith('.tsx') || value.endsWith('.js') || value.endsWith('.jsx');
}
function isTypeScriptSourceFile(filePath) {
    return (filePath.endsWith('.ts') || filePath.endsWith('.tsx')) && !filePath.endsWith('.d.ts');
}
function isIgnoredPath(rootPath, filePath) {
    const relPath = path.relative(rootPath, filePath).replace(/\\/g, '/');
    const segments = relPath.split('/').filter(segment => segment.length > 0);
    return segments.some(segment => ['.git', '.vs', '.vscode', '.meridian', 'bin', 'obj', 'node_modules', 'dist', 'build', 'coverage'].includes(segment.toLowerCase()));
}
function isSubdirectoryOf(candidate, parent) {
    const relative = path.relative(parent, candidate);
    return relative !== '.' && !relative.startsWith('..') && !path.isAbsolute(relative);
}
function hash(value) {
    return crypto.createHash('sha256').update(value, 'utf8').digest('hex').slice(0, 16);
}
function npmCommand() {
    return process.platform === 'win32' ? 'npm.cmd' : 'npm';
}
