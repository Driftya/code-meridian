import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import crypto from 'node:crypto';
import { findTypeScriptRoots } from '../services/project-discovery.js';
import { fileId } from '../walker/common.js';
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
    const nodes = distinct.map(finding => ({
        id: finding.id,
        name: `${finding.severity} ${finding.code}`,
        type: 'Diagnostic',
        namespace: finding.source,
        filePath: finding.filePath,
        lineNumber: finding.line,
        summary: finding.message,
        projectContext: projectName,
    }));
    const edges = distinct.map(finding => ({
        sourceId: fileId(projectName, finding.filePath),
        targetId: finding.id,
        type: 'Contains',
    }));
    await client.ingestNodes(nodes);
    await client.ingestEdges(edges);
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
    const useShell = process.platform === 'win32' && fileName.endsWith('.cmd');
    const child = spawn(fileName, arguments_, {
        cwd: workingDirectory,
        shell: useShell,
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
        child.on('error', error => {
            stderr += `${error.message}\n`;
            resolve(1);
        });
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
function looksLikePath(value) {
    return value.includes('/') || value.includes('\\') || value.endsWith('.ts') || value.endsWith('.tsx') || value.endsWith('.js') || value.endsWith('.jsx');
}
function hash(value) {
    return crypto.createHash('sha256').update(value, 'utf8').digest('hex').slice(0, 16);
}
function npmCommand() {
    return process.platform === 'win32' ? 'npm.cmd' : 'npm';
}
export const __testing = {
    buildDiagnosticFileSourceId: fileId,
};
