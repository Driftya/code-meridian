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

let useEmbeddings = false;
useEmbeddings = await client.isEmbeddingAvailable();
if (!useEmbeddings) {
  console.warn(
    '⚠️  Backend embedding service is unavailable. Indexing without embeddings. ' +
    'Ensure the CodeMeridian backend can reach its configured embedding provider.'
  );
}

if (clear) {
  process.stdout.write(`Clearing existing knowledge for '${projectName}'... `);
  await client.clearProject(projectName);
  console.log('done');
}

console.log(`Walking TypeScript files in ${rootPath}...`);
const { nodes, edges } = walkTypeScript(rootPath, projectName);
console.log(`  Found ${nodes.length} nodes, ${edges.length} edges`);

// Ingest nodes with optional embeddings
let nodeCount = 0;
let nodeErrors = 0;
const embeddableTypes = ['Class', 'Interface', 'Method', 'Enum'];
for (const node of nodes) {
  try {
    // Generate embedding for important node types
    if (useEmbeddings && embeddableTypes.includes(node.type)) {
      const embeddingText = `${node.type} ${node.name}${node.summary ? ` - ${node.summary}` : ''}${
        node.namespace ? ` in ${node.namespace}` : ''
      }`;
      const embedding = await client.generateEmbedding(embeddingText);
      if (embedding && embedding.length > 0) {
        node.embeddingCsv = embedding.join(',');
      }
    }
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

  const reindex = (): void => {
    clearTimeout(debounceTimer);
    debounceTimer = setTimeout(async () => {
      console.log('[watch] Change detected — re-indexing...');
      try {
        const result = walkTypeScript(rootPath, projectName!);
        for (const node of result.nodes) {
          if (useEmbeddings && embeddableTypes.includes(node.type)) {
            const embeddingText = `${node.type} ${node.name}${node.summary ? ` - ${node.summary}` : ''}${
              node.namespace ? ` in ${node.namespace}` : ''
            }`;
            const embedding = await client.generateEmbedding(embeddingText);
            if (embedding && embedding.length > 0) {
              node.embeddingCsv = embedding.join(',');
            }
          }
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
    .on('add', reindex)
    .on('change', reindex)
    .on('unlink', reindex);

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
  -h, --help          Show this help

Examples:
  npx tsx src/index.ts C:\\Projects\\MyApp
  npx tsx src/index.ts ../my-api --clear
  npx tsx src/index.ts . --url http://localhost:5100
`);
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
