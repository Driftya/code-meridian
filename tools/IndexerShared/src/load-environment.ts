import fs from 'node:fs';
import path from 'node:path';

export function loadEnvironmentForInvocation(rootPath: string, cwd = process.cwd()): void {
  const currentEnvPath = findUpEnvFile(cwd);
  const targetEnvPath = findUpEnvFile(rootPath);

  if (currentEnvPath) {
    loadEnvFile(currentEnvPath, false);
  }

  if (targetEnvPath && normalizePath(targetEnvPath) !== normalizePath(currentEnvPath)) {
    loadEnvFile(targetEnvPath, true);
  }
}

function normalizePath(filePath?: string): string | undefined {
  return filePath ? path.resolve(filePath) : undefined;
}

function findUpEnvFile(startDirectory: string): string | undefined {
  for (let current = path.resolve(startDirectory); ; ) {
    const candidate = path.join(current, '.env');
    if (fs.existsSync(candidate)) {
      return candidate;
    }

    const parent = path.dirname(current);
    if (parent === current) {
      return undefined;
    }

    current = parent;
  }
}

function loadEnvFile(filePath: string, override: boolean): void {
  const content = fs.readFileSync(filePath, 'utf8');

  for (const line of content.split(/\r?\n/)) {
    const parsed = parseEnvLine(line);
    if (!parsed) {
      continue;
    }

    if (!override && process.env[parsed.key] !== undefined) {
      continue;
    }

    process.env[parsed.key] = parsed.value;
  }
}

function parseEnvLine(line: string): { key: string; value: string } | undefined {
  const trimmed = line.trim();
  if (!trimmed || trimmed.startsWith('#')) {
    return undefined;
  }

  const separatorIndex = trimmed.indexOf('=');
  if (separatorIndex <= 0) {
    return undefined;
  }

  const key = trimmed.slice(0, separatorIndex).trim();
  if (!key) {
    return undefined;
  }

  let value = trimmed.slice(separatorIndex + 1).trim();
  if (
    value.length >= 2 &&
    ((value.startsWith('"') && value.endsWith('"')) ||
      (value.startsWith("'") && value.endsWith("'")))
  ) {
    value = value.slice(1, -1);
  }

  return { key, value };
}
