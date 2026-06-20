import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { walkFrontend } from '../src/walker.js';

describe('walkFrontend', () => {
  const createdRoots: string[] = [];

  afterEach(() => {
    for (const root of createdRoots.splice(0, createdRoots.length)) {
      fs.rmSync(root, { recursive: true, force: true });
    }
  });

  it('links html usage and stylesheet selectors through shared class concepts', () => {
    const rootPath = fs.mkdtempSync(path.join(os.tmpdir(), 'codemeridian-html-css-walker-'));
    createdRoots.push(rootPath);

    fs.mkdirSync(path.join(rootPath, 'src'), { recursive: true });
    const htmlPath = path.join(rootPath, 'src', 'index.html');
    const cssPath = path.join(rootPath, 'src', 'site.css');

    fs.writeFileSync(
      htmlPath,
      '<link rel="stylesheet" href="./site.css"><div class="hero card" id="main-panel"></div>',
    );
    fs.writeFileSync(
      cssPath,
      '.hero { color: red; }\n.hero { color: blue; }\n.layout .hero { color: navy; }\n#main-panel { padding: 1rem; }\n.card { color: var(--brand); }\n:root { --brand: #fff; }',
    );

    const result = walkFrontend(rootPath, 'CodeMeridian', [htmlPath, cssPath]);

    const classNodes = result.nodes.filter(node => node.properties?.externalKind === 'CssClass').map(node => node.name);
    expect(classNodes).toEqual(expect.arrayContaining(['hero', 'card']));

    const selectorNodes = result.nodes.filter(node => node.properties?.externalKind === 'CssSelector').map(node => node.name);
    expect(selectorNodes).toEqual(expect.arrayContaining(['.hero', '.layout .hero', '#main-panel', '.card', ':root']));

    const selectorNode = result.nodes.find(node => node.name === '.layout .hero');
    expect(selectorNode?.properties).toEqual(expect.objectContaining({
      specificity: '0,2,0',
      specificityScore: '20',
      sourceOrder: '3',
      targetClassConceptsCsv: 'layout,hero',
    }));

    const declarationNodes = result.nodes.filter(node => node.properties?.externalKind === 'CssDeclaration');
    expect(declarationNodes).toEqual(expect.arrayContaining([
      expect.objectContaining({
        name: 'color: red',
        properties: expect.objectContaining({
          propertyName: 'color',
          rawValue: 'red',
          selectorText: '.hero',
        }),
      }),
      expect.objectContaining({
        name: 'padding: 1rem',
        properties: expect.objectContaining({
          propertyName: 'padding',
          rawValue: '1rem',
          selectorText: '#main-panel',
        }),
      }),
      expect.objectContaining({
        name: 'color: navy',
        properties: expect.objectContaining({
          selectorText: '.layout .hero',
          specificity: '0,2,0',
          sourceOrder: '3',
          targetClassConceptsCsv: 'layout,hero',
        }),
      }),
    ]));

    expect(result.edges).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ type: 'UsesClass' }),
        expect.objectContaining({ type: 'DefinesSelector' }),
        expect.objectContaining({ type: 'ImportsStyle' }),
        expect.objectContaining({ type: 'Uses', properties: expect.objectContaining({ relationshipKind: 'DefinesStyleDeclaration' }) }),
        expect.objectContaining({ type: 'UsesId' }),
        expect.objectContaining({ type: 'DefinesCssVariable' }),
        expect.objectContaining({ type: 'UsesCssVariable' }),
        expect.objectContaining({
          type: 'Overrides',
          properties: expect.objectContaining({
            relationshipKind: 'LikelyCssCascadeOverride',
            sharedTargetKind: 'CssClass',
            sharedTargetName: 'hero',
          }),
        }),
      ]),
    );
  });

  it('extracts static tsx className usage and stylesheet imports', () => {
    const rootPath = fs.mkdtempSync(path.join(os.tmpdir(), 'codemeridian-html-css-tsx-'));
    createdRoots.push(rootPath);

    fs.mkdirSync(path.join(rootPath, 'src'), { recursive: true });
    const tsxPath = path.join(rootPath, 'src', 'Card.tsx');
    const scssPath = path.join(rootPath, 'src', 'Card.scss');

    fs.writeFileSync(
      tsxPath,
      [
        "import './Card.scss';",
        'export function Card() {',
        '  return <section className={`card ${"card--wide"}`} id="card-root" />;',
        '}',
      ].join('\n'),
    );
    fs.writeFileSync(scssPath, '.card { padding: 1rem; }\n.card--wide { margin: 0; }');

    const result = walkFrontend(rootPath, 'CodeMeridian', [tsxPath, scssPath]);

    const classUsageEdges = result.edges.filter(edge => edge.type === 'UsesClass');
    const importEdges = result.edges.filter(edge => edge.type === 'ImportsStyle');
    const idEdges = result.edges.filter(edge => edge.type === 'UsesId');

    expect(classUsageEdges.length).toBeGreaterThanOrEqual(3);
    expect(importEdges).toContainEqual(expect.objectContaining({ type: 'ImportsStyle' }));
    expect(idEdges).toContainEqual(expect.objectContaining({ type: 'UsesId' }));
  });
});
