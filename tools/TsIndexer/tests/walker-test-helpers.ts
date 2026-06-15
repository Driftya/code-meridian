import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach } from 'vitest';

export interface TempProjectHarness {
  getRootPath(): string;
  writeFile(relativePath: string, content: string): void;
  listTypeScriptFiles(): string[];
}

export function useTempProject(prefix = 'codemeridian-ts-indexer-'): TempProjectHarness {
  let rootPath = '';

  beforeEach(() => {
    rootPath = fs.mkdtempSync(path.join(os.tmpdir(), prefix));
  });

  afterEach(() => {
    fs.rmSync(rootPath, { recursive: true, force: true });
  });

  return {
    getRootPath: () => rootPath,
    writeFile: (relativePath: string, content: string) => {
      const filePath = path.join(rootPath, relativePath);
      fs.mkdirSync(path.dirname(filePath), { recursive: true });
      fs.writeFileSync(filePath, content);
    },
    listTypeScriptFiles: () =>
      listTypeScriptFiles(rootPath),
  };
}

function listTypeScriptFiles(rootPath: string): string[] {
  const results: string[] = [];
  const pending = [rootPath];

  while (pending.length > 0) {
    const current = pending.pop();
    if (!current) {
      continue;
    }

    for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
      const fullPath = path.join(current, entry.name);
      if (entry.isDirectory()) {
        if (isIgnoredDirectory(entry.name)) {
          continue;
        }

        pending.push(fullPath);
        continue;
      }

      if (entry.isFile() && isTypeScriptSourceFile(entry.name)) {
        results.push(fullPath);
      }
    }
  }

  return results.sort((left, right) => left.localeCompare(right));
}

function isIgnoredDirectory(name: string): boolean {
  return new Set(['.git', '.vs', '.vscode', '.meridian', 'bin', 'obj', 'node_modules', 'dist', 'build', 'coverage'])
    .has(name.toLowerCase());
}

function isTypeScriptSourceFile(name: string): boolean {
  const normalizedName = name.toLowerCase();
  return (normalizedName.endsWith('.ts') || normalizedName.endsWith('.tsx')) &&
    !normalizedName.endsWith('.d.ts');
}
