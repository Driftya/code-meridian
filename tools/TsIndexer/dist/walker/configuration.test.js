import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { walkTypeScript } from '../walker.js';
describe('TypeScript configuration extraction', () => {
    const workspaces = [];
    afterEach(() => {
        for (const workspace of workspaces.splice(0)) {
            fs.rmSync(workspace, { recursive: true, force: true });
        }
    });
    it('indexes process.env property and element access as ReadsConfig', () => {
        const root = createWorkspace();
        writeFile(root, 'tsconfig.json', '{"compilerOptions":{"target":"ES2022","module":"ESNext"}}');
        writeFile(root, 'src/env.ts', [
            'export function readConfig() {',
            '  const a = process.env.NEO4J__URI;',
            "  const b = process.env['CodeMeridian__Auth__ApiKey'];",
            '  return { a, b };',
            '}',
        ].join('\n'));
        const result = walkTypeScript(root, 'CodeMeridian', listTypeScriptFiles(root));
        expect(result.nodes).toContainEqual(expect.objectContaining({
            id: 'CodeMeridian::ConfigurationKey::NEO4J:URI',
            type: 'ConfigurationKey',
        }));
        expect(result.nodes).toContainEqual(expect.objectContaining({
            id: 'CodeMeridian::ConfigurationKey::CodeMeridian:Auth:ApiKey',
            type: 'ConfigurationKey',
        }));
        expect(result.edges).toContainEqual(expect.objectContaining({
            type: 'ReadsConfig',
            targetId: 'CodeMeridian::ConfigurationKey::NEO4J:URI',
            properties: expect.objectContaining({ rawKey: 'NEO4J__URI', accessPattern: 'process.env' }),
        }));
        expect(result.edges).toContainEqual(expect.objectContaining({
            type: 'ReadsConfig',
            targetId: 'CodeMeridian::ConfigurationKey::CodeMeridian:Auth:ApiKey',
            properties: expect.objectContaining({ rawKey: 'CodeMeridian__Auth__ApiKey', accessPattern: 'process.env[indexer]' }),
        }));
    });
    it('indexes import.meta.env destructuring as ReadsConfig', () => {
        const root = createWorkspace();
        writeFile(root, 'tsconfig.json', '{"compilerOptions":{"target":"ES2022","module":"ESNext"}}');
        writeFile(root, 'src/client.ts', [
            'export const url = (() => {',
            '  const { VITE_API_URL, VITE_AUTH__TOKEN: token } = import.meta.env;',
            '  return `${VITE_API_URL}:${token}`;',
            '})();',
        ].join('\n'));
        const result = walkTypeScript(root, 'CodeMeridian', listTypeScriptFiles(root));
        expect(result.edges).toContainEqual(expect.objectContaining({
            type: 'ReadsConfig',
            targetId: 'CodeMeridian::ConfigurationKey::VITE_API_URL',
            properties: expect.objectContaining({ accessPattern: 'import.meta.env destructure' }),
        }));
        expect(result.edges).toContainEqual(expect.objectContaining({
            type: 'ReadsConfig',
            targetId: 'CodeMeridian::ConfigurationKey::VITE_AUTH:TOKEN',
            properties: expect.objectContaining({ rawKey: 'VITE_AUTH__TOKEN' }),
        }));
    });
    it('indexes env schema assignment as BindsConfig', () => {
        const root = createWorkspace();
        writeFile(root, 'tsconfig.json', '{"compilerOptions":{"target":"ES2022","module":"ESNext"}}');
        writeFile(root, 'src/schema.ts', [
            'const appConfig = { env: {} as Record<string, string | undefined> };',
            'const z = { object: (value: unknown) => value };',
            'appConfig.env = z.object({ NEO4J__URI: true });',
        ].join('\n'));
        const result = walkTypeScript(root, 'CodeMeridian', listTypeScriptFiles(root));
        expect(result.edges).toContainEqual(expect.objectContaining({
            type: 'BindsConfig',
            targetId: 'CodeMeridian::ConfigurationKey::NEO4J:URI',
            properties: expect.objectContaining({
                rawKey: 'NEO4J__URI',
                accessPattern: 'env schema',
                optionsType: 'appConfig',
            }),
        }));
    });
    function createWorkspace() {
        const root = fs.mkdtempSync(path.join(os.tmpdir(), 'codemeridian-ts-config-'));
        workspaces.push(root);
        return root;
    }
    function writeFile(root, relativePath, content) {
        const fullPath = path.join(root, relativePath);
        fs.mkdirSync(path.dirname(fullPath), { recursive: true });
        fs.writeFileSync(fullPath, content, 'utf8');
    }
    function listTypeScriptFiles(root) {
        return [
            path.join(root, 'src', 'client.ts'),
            path.join(root, 'src', 'env.ts'),
            path.join(root, 'src', 'schema.ts'),
        ].filter(filePath => fs.existsSync(filePath));
    }
});
