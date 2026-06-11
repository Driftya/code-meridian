import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach } from 'vitest';

export interface TempProjectHarness {
  getRootPath(): string;
  writeFile(relativePath: string, content: string): void;
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
  };
}
