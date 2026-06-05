import path from 'path';
import fs from 'fs';
import { watch as chokidarWatch } from 'chokidar';
import { walkTypeScript } from './walker.js';
import { CodeMeridianClient } from './client.js';

// ── CLI args ──────────────────────────────────────────────────────────────────

const args = process.argv.slice(2);

if (args.length === 0 || args.includes('-h') || args.includes('--help')) {
  printUsage();
  process.exit(0);
}

const positional: string[] = [];
let projectName: string | undefined;
loadDotEnv();
let serverUrl = process.env.CodeMeridian_Url ?? 'http://localhost:5100';
const apiKey = process.env.CodeMeridian_Auth_ApiKey;
let clear = false;
let noDocs = false;
let watch = false;
let filesListPath: string | undefined;

for (let i = 0; i < args.length; i++) {
  switch (args[i]) {
    case '--project':
      projectName = args[++i];
      break;
    case '--url':
      serverUrl = args[++i];
      break;
    case '--clear':
      clear = true;
      break;
    case '--no-docs':
      noDocs = true;
      break;
    case '--watch':
      watch = true;
      break;
    case '--files-list':
      filesListPath = args[++i];
      break;
    default:
      if (!args[i].startsWith('--')) positional.push(args[i]);
  }
}

if (!positional[0]) {
  console.error('error: <path> is required');
  printUsage();
  process.exit(1);
}

const rootPath = path.resolve(positional[0]);
if (!fs.existsSync(rootPath)) {
  console.error(`error: directory not found: ${rootPath}`);
  process.exit(1);
}

if (!projectName) {
  projectName = resolveProjectName(rootPath);
  console.log(`info: --project not specified, resolved to '${projectName}'`);
}

// ── Run ───────────────────────────────────────────────────────────────────────

const client = new CodeMeridianClient(serverUrl, apiKey);

if (clear) {
  process.stdout.write(`Clearing existing knowledge for '${projectName}'... `);
  await client.clearProject(projectName);
  console.log('done');
}

console.log(`Walking TypeScript files in ${rootPath}...`);
const files = filesListPath ? readFilesList(filesListPath) : undefined;
if (files) console.log(`  Incremental file list: ${files.length} file(s)`);
const { nodes, edges } = walkTypeScript(rootPath, projectName, files);
console.log(`  Found ${nodes.length} nodes, ${edges.length} edges`);

let nodeCount = 0;
let nodeErrors = 0;
for (const node of nodes) {
  try {
    await client.ingestNode(node);
    nodeCount++;
  } catch (e) {
    nodeErrors++;
    if (nodeErrors <= 5) console.warn(`  warn: node ${node.id}: ${e}`);
  }
}
console.log(`  Ingested ${nodeCount} nodes${nodeErrors > 0 ? ` (${nodeErrors} errors)` : ''}`);

// Ingest edges
let edgeCount = 0;
let edgeErrors = 0;
for (const edge of edges) {
  try {
    await client.ingestEdge(edge);
    edgeCount++;
  } catch (e) {
    edgeErrors++;
    if (edgeErrors <= 5) console.warn(`  warn: edge ${edge.sourceId} → ${edge.targetId}: ${e}`);
  }
}
console.log(`  Ingested ${edgeCount} edges${edgeErrors > 0 ? ` (${edgeErrors} errors)` : ''}`);

// Ingest README
if (!noDocs) {
  const readmePath = path.join(rootPath, 'README.md');
  if (fs.existsSync(readmePath)) {
    const content = fs.readFileSync(readmePath, 'utf-8');
    await client.ingestDocument({ source: 'README.md', content, projectContext: projectName });
    console.log('  Ingested README.md');
  }
}

console.log(`\nDone. '${projectName}' indexed into CodeMeridian at ${serverUrl}`);

// ── Watch mode ────────────────────────────────────────────────────────────────

if (watch) {
  console.log(`\nWatch mode active — monitoring ${rootPath} for .ts/.tsx/.md changes. Press Ctrl+C to exit.\n`);

  let debounceTimer: ReturnType<typeof setTimeout> | undefined;
  const changed = new Set<string>();
  const deleted = new Set<string>();

  const reindex = (filePath: string, wasDeleted: boolean): void => {
    const relativePath = path.relative(rootPath, filePath).replace(/\\/g, '/');
    if (wasDeleted) deleted.add(relativePath);
    else changed.add(filePath);

    clearTimeout(debounceTimer);
    debounceTimer = setTimeout(async () => {
      console.log('[watch] Change detected — re-indexing...');
      const changedBatch = Array.from(changed);
      const deletedBatch = Array.from(deleted);
      changed.clear();
      deleted.clear();

      try {
        for (const relPath of [...changedBatch.map(file => path.relative(rootPath, file).replace(/\\/g, '/')), ...deletedBatch]) {
          await client.deleteProjectFile(projectName!, relPath).catch(() => {});
        }

        const result = changedBatch.length > 0
          ? walkTypeScript(rootPath, projectName!, changedBatch)
          : { nodes: [], edges: [] };
        for (const node of result.nodes) {
          await client.ingestNode(node).catch(() => {});
        }
        for (const edge of result.edges) await client.ingestEdge(edge).catch(() => {});
        console.log(`[watch] Done: ${result.nodes.length} nodes, ${result.edges.length} edges`);
      } catch (e) {
        console.error('[watch] Re-index failed:', e);
      }
    }, 2_000); // 2 s debounce
  };

  chokidarWatch(rootPath, {
    ignored: [
      /(^|[/\\])\../,            // dotfiles
      /node_modules/,
      /dist/,
      /build/,
      /\.d\.ts$/,
    ],
    persistent: true,
    ignoreInitial: true,
  })
    .on('add', filePath => reindex(filePath, false))
    .on('change', filePath => reindex(filePath, false))
    .on('unlink', filePath => reindex(filePath, true));

  // Keep process alive until Ctrl+C
  await new Promise<void>((resolve) => {
    process.on('SIGINT', () => { console.log('\nWatch mode stopped.'); resolve(); });
    process.on('SIGTERM', () => resolve());
  });
}

// ── Help ──────────────────────────────────────────────────────────────────────

function printUsage(): void {
  console.log(`
Usage:
  npx tsx src/index.ts <path> [--project <name>] [options]

Arguments:
  <path>              Root directory of the TypeScript project to index

Options:
  --project <name>    Short identifier for this project in the graph.
                      If omitted, auto-detected from package.json "name",
                      .code-workspace filename, or the folder name.
  --url <url>         CodeMeridian server URL  (default: CodeMeridian_Url or http://localhost:5100)
  --clear             Wipe this project's existing data before indexing
  --no-docs           Skip README ingestion
  --watch             Stay running; re-index when .ts, .tsx or .md files change
  --files-list <path> Index only files listed in a newline-delimited text file
  -h, --help          Show this help

Examples:
  npx tsx src/index.ts C:\\Projects\\MyApp
  npx tsx src/index.ts ../my-api --clear
  npx tsx src/index.ts . --url http://localhost:5100
`);
}

function readFilesList(filePath: string): string[] {
  return fs
    .readFileSync(path.resolve(filePath), 'utf8')
    .split(/\r?\n/)
    .map(line => line.trim())
    .filter(line => line.length > 0);
}

function resolveProjectName(dir: string): string {
  // 1. package.json "name"
  const pkg = path.join(dir, 'package.json');
  if (fs.existsSync(pkg)) {
    try {
      const name = JSON.parse(fs.readFileSync(pkg, 'utf8')).name as string | undefined;
      if (name) return name;
    } catch { /* ignore malformed package.json */ }
  }

  // 2. .code-workspace
  const workspaces = fs.readdirSync(dir).filter(f => f.endsWith('.code-workspace'));
  if (workspaces.length > 0) return path.basename(workspaces[0], '.code-workspace');

  // 3. folder name
  return path.basename(dir);
}

function loadDotEnv(): void {
  const envPath = findDotEnv(process.cwd());
  if (!envPath) return;

  const lines = fs.readFileSync(envPath, 'utf8').split(/\r?\n/);
  for (const rawLine of lines) {
    let line = rawLine.trim();
    if (!line || line.startsWith('#')) continue;

    if (line.toLowerCase().startsWith('export ')) {
      line = line.slice('export '.length).trimStart();
    }

    const separator = line.indexOf('=');
    if (separator <= 0) continue;

    const key = line.slice(0, separator).trim();
    let value = line.slice(separator + 1).trim();
    if (!key || process.env[key] !== undefined) continue;

    if (value.length >= 2 && value.startsWith('"') && value.endsWith('"')) {
      value = value.slice(1, -1).replace(/\\"/g, '"');
    } else if (value.length >= 2 && value.startsWith("'") && value.endsWith("'")) {
      value = value.slice(1, -1);
    }

    process.env[key] = value;
  }
}

function findDotEnv(startDir: string): string | undefined {
  let current = path.resolve(startDir);

  while (true) {
    const candidate = path.join(current, '.env');
    if (fs.existsSync(candidate)) return candidate;

    const parent = path.dirname(current);
    if (parent === current) return undefined;
    current = parent;
  }
}
