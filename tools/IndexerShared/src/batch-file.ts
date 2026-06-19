import fs from 'node:fs';
import path from 'node:path';

export interface IndexerBatchFile {
  files: string[];
  fileRoles: Map<string, string>;
}

interface IndexerBatchEntry {
  path?: string;
  Path?: string;
  fileRole?: string;
  FileRole?: string;
}

export function readIndexerBatchFile(rootPath: string, batchFilePath: string): IndexerBatchFile {
  const payload = JSON.parse(fs.readFileSync(batchFilePath, 'utf8')) as IndexerBatchEntry[];

  const files: string[] = [];
  const fileRoles = new Map<string, string>();

  for (const entry of payload) {
    const rawPath = entry.path ?? entry.Path;
    if (!rawPath) {
      throw new Error(`Batch file '${batchFilePath}' contains an entry without a path.`);
    }

    const normalizedEntryPath = rawPath.replace(/[\\/]/g, path.sep);
    const fullPath = path.resolve(rootPath, normalizedEntryPath);
    files.push(fullPath);

    const fileRole = entry.fileRole ?? entry.FileRole;
    if (fileRole) {
      const relativePath = path.relative(rootPath, fullPath).replace(/\\/g, '/');
      fileRoles.set(relativePath, fileRole);
    }
  }

  return { files, fileRoles };
}
