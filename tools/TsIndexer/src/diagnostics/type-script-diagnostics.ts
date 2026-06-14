import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import crypto from 'node:crypto';
import type { CodeMeridianClient } from '../client.js';
import { findTypeScriptRoots } from '../services/project-discovery.js';
import { fileId } from '../walker/common.js';

export interface DiagnosticFinding {
  id: string;
  severity: string;
  code: string;
  message: string;
  filePath: string;
  line?: number;
  column?: number;
  source: string;
}

export async function indexTypeScriptDiagnostics(
  client: CodeMeridianClient,
  rootPath: string,
  projectName: string,
): Promise<number> {
  const roots = findTypeScriptRoots(rootPath);
  const findings: DiagnosticFinding[] = [];

  for (const typeScriptRoot of roots) {
    const tsc = resolveLocalNodeBinary(typeScriptRoot, 'tsc');
    if (!tsc) {
      console.log(`  TypeScript diagnostics unavailable in ${path.relative(rootPath, typeScriptRoot)}: local tsc not found.`);
      continue;
    }

    const result = await runCaptureAsync(
      tsc,
      ['--noEmit', '--pretty', 'false', '--noUnusedLocals', '--noUnusedParameters'],
      typeScriptRoot,
    );
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
      sourceId: fileId(projectName, finding.filePath),
      targetId: finding.id,
      type: 'Contains',
    });
  }

  return distinct.length;
}

export function parseTypeScriptDiagnostics(
  output: string,
  rootPath: string,
  workingDirectory: string,
  project: string,
): DiagnosticFinding[] {
  const findings: DiagnosticFinding[] = [];
  const pattern = /^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\):\s(?<severity>error|warning)\s(?<code>TS\d+):\s(?<message>.+)$/i;

  for (const rawLine of output.split(/[\r\n]+/)) {
    const match = pattern.exec(rawLine.trimEnd());
    if (!match?.groups) continue;

    findings.push(createDiagnostic(
      project,
      'tsc',
      match.groups.severity,
      match.groups.code,
      match.groups.message.trim(),
      normalizePath(match.groups.file, rootPath, workingDirectory),
      Number.parseInt(match.groups.line, 10),
      Number.parseInt(match.groups.column, 10),
    ));
  }

  return findings;
}

export function parseLintDiagnostics(
  output: string,
  rootPath: string,
  workingDirectory: string,
  project: string,
): DiagnosticFinding[] {
  const findings: DiagnosticFinding[] = [];
  const pattern = /^\s*(?<line>\d+):(?<column>\d+)\s+(?<severity>error|warning|warn)\s+(?<message>.+?)\s+(?<code>[@\w/-]+)$/i;
  let currentFile: string | undefined;

  for (const rawLine of output.split(/[\r\n]+/)) {
    const line = rawLine.trimEnd();
    if (line.length === 0) continue;

    if (!line.startsWith(' ') && looksLikePath(line)) {
      currentFile = normalizePath(line, rootPath, workingDirectory);
      continue;
    }

    if (!currentFile) continue;

    const match = pattern.exec(line);
    if (!match?.groups) continue;

    findings.push(createDiagnostic(
      project,
      'eslint',
      match.groups.severity,
      match.groups.code,
      match.groups.message.trim(),
      currentFile,
      Number.parseInt(match.groups.line, 10),
      Number.parseInt(match.groups.column, 10),
    ));
  }

  return findings;
}

async function runCaptureAsync(
  fileName: string,
  arguments_: string[],
  workingDirectory: string,
): Promise<{ exitCode: number; output: string }> {
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

  const exitCode = await new Promise<number>(resolve => {
    child.on('error', error => {
      stderr += `${error.message}\n`;
      resolve(1);
    });
    child.on('close', code => resolve(code ?? 0));
  });

  return { exitCode, output: `${stdout}\n${stderr}`.trim() };
}

function createDiagnostic(
  project: string,
  source: string,
  severity: string,
  code: string,
  message: string,
  filePath: string,
  line?: number,
  column?: number,
): DiagnosticFinding {
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

function normalizePath(filePath: string, rootPath: string, workingDirectory: string): string {
  const trimmed = filePath.trim().replace(/^"|"$/g, '');
  const fullPath = path.isAbsolute(trimmed) ? trimmed : path.resolve(workingDirectory, trimmed);
  return path.relative(rootPath, fullPath).replace(/\\/g, '/');
}

function resolveLocalNodeBinary(rootPath: string, name: string): string | undefined {
  const executable = process.platform === 'win32' ? `${name}.cmd` : name;
  for (let current = rootPath; ; current = path.dirname(current)) {
    const candidate = path.join(current, 'node_modules', '.bin', executable);
    if (fs.existsSync(candidate)) return candidate;

    const parent = path.dirname(current);
    if (parent === current) return undefined;
  }
}

function resolveLintCommand(rootPath: string): { fileName: string; arguments: string[] } | undefined {
  const packageJson = path.join(rootPath, 'package.json');
  if (fs.existsSync(packageJson)) {
    try {
      const content = JSON.parse(fs.readFileSync(packageJson, 'utf8')) as { scripts?: Record<string, string> };
      if (content.scripts?.lint) {
        return { fileName: npmCommand(), arguments: ['run', 'lint'] };
      }
    } catch {
      // Ignore malformed package.json and fall back to local eslint.
    }
  }

  const eslint = resolveLocalNodeBinary(rootPath, 'eslint');
  return eslint ? { fileName: eslint, arguments: ['.'] } : undefined;
}

function looksLikePath(value: string): boolean {
  return value.includes('/') || value.includes('\\') || value.endsWith('.ts') || value.endsWith('.tsx') || value.endsWith('.js') || value.endsWith('.jsx');
}

function hash(value: string): string {
  return crypto.createHash('sha256').update(value, 'utf8').digest('hex').slice(0, 16);
}

function npmCommand(): string {
  return process.platform === 'win32' ? 'npm.cmd' : 'npm';
}

export const __testing = {
  buildDiagnosticFileSourceId: fileId,
};
