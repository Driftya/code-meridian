import fs from 'node:fs';
import path from 'node:path';
import postcss from 'postcss';
import postcssScss from 'postcss-scss';
import type { CodeEdgeDto, CodeNodeDto } from '#indexer-shared';
import { addNode, fileNodeId, frontendConceptId, hashText, lineCountFromContent, normalizeImportTarget, selectorNodeId, styleDeclarationNodeId, toRelativePath } from './common.js';

const CLASS_SELECTOR = /\.([_a-zA-Z][\w-]*)/g;
const ID_SELECTOR = /#([_a-zA-Z][\w-]*)/g;
const CSS_VARIABLE = /var\(\s*(--[\w-]+)\s*(?:,[^)]+)?\)/g;
const STYLE_IMPORT = /(?:url\()?["']([^"']+\.(?:css|scss|sass))(?:["']\))?/i;
const ATTRIBUTE_SELECTOR = /\[[^\]]+\]/g;
const PSEUDO_ELEMENT_SELECTOR = /::[\w-]+(?:\([^)]*\))?/g;
const PSEUDO_CLASS_SELECTOR = /:(?!:)[\w-]+(?:\([^)]*\))?/g;
const WHERE_PSEUDO_SELECTOR = /:where\([^)]*\)/g;
const ELEMENT_SELECTOR = /(^|[\s>+~,(])([a-zA-Z][\w-]*)/g;

export function collectStyleArtifacts(
  rootPath: string,
  projectName: string,
  filePath: string,
  nodes: CodeNodeDto[],
  edges: CodeEdgeDto[],
  knownIds: Set<string>,
  resolveFileRole?: (relativePath: string) => string | undefined,
): void {
  const relativePath = toRelativePath(rootPath, filePath);
  const content = fs.readFileSync(filePath, 'utf8');
  const fileId = fileNodeId(projectName, relativePath);

  addNode(nodes, knownIds, {
    id: fileId,
    name: path.posix.basename(relativePath),
    type: 'File',
    filePath: relativePath,
    lineNumber: 1,
    lineCount: lineCountFromContent(content),
    summary: 'HTML/CSS/SCSS indexer stylesheet file.',
    sourceHash: hashText(content),
    projectContext: projectName,
    properties: {
      language: relativePath.endsWith('.scss') ? 'scss' : 'css',
      frontendRole: 'StyleSheetFile',
    },
  }, resolveFileRole);

  const root = relativePath.endsWith('.scss')
    ? postcssScss.parse(content, { from: filePath })
    : postcss.parse(content, { from: filePath });
  let sourceOrder = 0;
  const declarations: IndexedStyleDeclaration[] = [];

  root.walkAtRules(atRule => {
    if (!['import', 'use', 'forward'].includes(atRule.name))
      return;

    const target = normalizeImportTarget(atRule.params.match(STYLE_IMPORT)?.[1] ?? atRule.params);
    if (!target)
      return;

    const importedPath = resolveImportedStylePath(path.dirname(filePath), target);
    if (!importedPath)
      return;

    const importedRelativePath = toRelativePath(rootPath, importedPath);
    addNode(nodes, knownIds, {
      id: fileNodeId(projectName, importedRelativePath),
      name: path.posix.basename(importedRelativePath),
      type: 'File',
      filePath: importedRelativePath,
      projectContext: projectName,
      properties: {
        frontendRole: 'StyleSheetFile',
      },
    }, resolveFileRole);

    edges.push({
      sourceId: fileId,
      targetId: fileNodeId(projectName, importedRelativePath),
      type: 'ImportsStyle',
      callSite: `${relativePath}:${atRule.source?.start?.line ?? 1}`,
    });
  });

  root.walkRules(rule => {
    const selectorText = rule.selector?.trim();
    if (!selectorText)
      return;

    const lineNumber = rule.source?.start?.line ?? 1;
    sourceOrder++;
    const selectorOrder = sourceOrder;
    const specificity = computeSelectorSpecificity(selectorText);
    const selectorId = selectorNodeId(projectName, relativePath, selectorText, lineNumber);
    const classTargets = extractAll(selectorText, CLASS_SELECTOR);
    const idTargets = extractAll(selectorText, ID_SELECTOR);
    addNode(nodes, knownIds, {
      id: selectorId,
      name: selectorText,
      type: 'ExternalConcept',
      filePath: relativePath,
      lineNumber,
      lineCount: 1,
      projectContext: projectName,
      properties: {
        externalKind: 'CssSelector',
        selectorText,
        specificity: specificity.display,
        specificityA: String(specificity.ids),
        specificityB: String(specificity.classes),
        specificityC: String(specificity.elements),
        specificityScore: String(specificity.score),
        specificityInference: 'bounded-static',
        sourceOrder: String(selectorOrder),
        targetClassConceptsCsv: classTargets.join(','),
        targetIdConceptsCsv: idTargets.join(','),
      },
    }, resolveFileRole);

    edges.push({
      sourceId: fileId,
      targetId: selectorId,
      type: 'DefinesSelector',
      callSite: `${relativePath}:${lineNumber}`,
    });

    for (const className of classTargets) {
      const classId = frontendConceptId(projectName, 'CssClass', className);
      addNode(nodes, knownIds, {
        id: classId,
        name: className,
        type: 'ExternalConcept',
        projectContext: projectName,
        properties: {
          externalKind: 'CssClass',
        },
      });

      edges.push({
        sourceId: selectorId,
        targetId: classId,
        type: 'UsesClass',
        callSite: `${relativePath}:${lineNumber}`,
      });
    }

    for (const idValue of idTargets) {
      const idNodeId = frontendConceptId(projectName, 'CssId', idValue);
      addNode(nodes, knownIds, {
        id: idNodeId,
        name: idValue,
        type: 'ExternalConcept',
        projectContext: projectName,
        properties: {
          externalKind: 'CssId',
        },
      });

      edges.push({
        sourceId: selectorId,
        targetId: idNodeId,
        type: 'UsesId',
        callSite: `${relativePath}:${lineNumber}`,
      });
    }

    rule.walkDecls(decl => {
      const declarationLine = decl.source?.start?.line ?? lineNumber;
      if (decl.prop.startsWith('--')) {
        const variableId = frontendConceptId(projectName, 'CssVariable', decl.prop);
        addNode(nodes, knownIds, {
          id: variableId,
          name: decl.prop,
          type: 'ExternalConcept',
          projectContext: projectName,
          properties: {
            externalKind: 'CssVariable',
            rawValue: decl.value,
          },
        });

        edges.push({
          sourceId: fileId,
          targetId: variableId,
          type: 'DefinesCssVariable',
          callSite: `${relativePath}:${declarationLine}`,
        });
      }

      const declarationId = styleDeclarationNodeId(projectName, relativePath, selectorText, decl.prop, declarationLine, decl.value);
      addNode(nodes, knownIds, {
        id: declarationId,
        name: `${decl.prop}: ${decl.value}`,
        type: 'ExternalConcept',
        filePath: relativePath,
        lineNumber: declarationLine,
        lineCount: 1,
        projectContext: projectName,
        properties: {
          externalKind: 'CssDeclaration',
          selectorText,
          propertyName: decl.prop,
          rawValue: decl.value,
          specificity: specificity.display,
          specificityA: String(specificity.ids),
          specificityB: String(specificity.classes),
          specificityC: String(specificity.elements),
          specificityScore: String(specificity.score),
          specificityInference: 'bounded-static',
          sourceOrder: String(selectorOrder),
          targetClassConceptsCsv: classTargets.join(','),
          targetIdConceptsCsv: idTargets.join(','),
        },
      }, resolveFileRole);
      declarations.push({
        id: declarationId,
        selectorText,
        propertyName: decl.prop,
        lineNumber: declarationLine,
        sourceOrder: selectorOrder,
        specificity,
        classTargets,
        idTargets,
      });

      edges.push({
        sourceId: selectorId,
        targetId: declarationId,
        type: 'Uses',
        callSite: `${relativePath}:${declarationLine}`,
        properties: {
          relationshipKind: 'DefinesStyleDeclaration',
        },
      });

      for (const variableName of extractAll(decl.value, CSS_VARIABLE)) {
        const variableId = frontendConceptId(projectName, 'CssVariable', variableName);
        addNode(nodes, knownIds, {
          id: variableId,
          name: variableName,
          type: 'ExternalConcept',
          projectContext: projectName,
          properties: {
            externalKind: 'CssVariable',
          },
        });

        edges.push({
          sourceId: selectorId,
          targetId: variableId,
          type: 'UsesCssVariable',
          callSite: `${relativePath}:${declarationLine}`,
        });
      }
    });
  });

  appendLikelyOverrideEdges(edges, declarations, relativePath);
}

interface SelectorSpecificity {
  ids: number;
  classes: number;
  elements: number;
  score: number;
  display: string;
}

interface IndexedStyleDeclaration {
  id: string;
  selectorText: string;
  propertyName: string;
  lineNumber: number;
  sourceOrder: number;
  specificity: SelectorSpecificity;
  classTargets: string[];
  idTargets: string[];
}

function extractAll(value: string, pattern: RegExp): string[] {
  const matches = new Set<string>();
  pattern.lastIndex = 0;
  let match: RegExpExecArray | null;
  while ((match = pattern.exec(value)) !== null) {
    if (match[1])
      matches.add(match[1]);
  }
  return [...matches];
}

function resolveImportedStylePath(baseDirectory: string, target: string): string | undefined {
  if (!target.startsWith('.'))
    return undefined;

  const candidates = [target];
  if (!path.extname(target)) {
    candidates.push(`${target}.css`, `${target}.scss`);
  }

  for (const candidate of candidates) {
    const fullPath = path.resolve(baseDirectory, candidate);
    if (fs.existsSync(fullPath))
      return fullPath;
  }

  return undefined;
}

function appendLikelyOverrideEdges(
  edges: CodeEdgeDto[],
  declarations: IndexedStyleDeclaration[],
  relativePath: string,
): void {
  const emitted = new Set<string>();
  const groups = new Map<string, Array<{ targetKind: 'CssClass' | 'CssId'; targetName: string; declaration: IndexedStyleDeclaration }>>();

  for (const declaration of declarations) {
    for (const className of declaration.classTargets) {
      const key = `${relativePath}|${declaration.propertyName}|CssClass|${className}`;
      const entry = { targetKind: 'CssClass' as const, targetName: className, declaration };
      const existing = groups.get(key);
      if (existing) {
        existing.push(entry);
      } else {
        groups.set(key, [entry]);
      }
    }

    for (const idValue of declaration.idTargets) {
      const key = `${relativePath}|${declaration.propertyName}|CssId|${idValue}`;
      const entry = { targetKind: 'CssId' as const, targetName: idValue, declaration };
      const existing = groups.get(key);
      if (existing) {
        existing.push(entry);
      } else {
        groups.set(key, [entry]);
      }
    }
  }

  for (const group of groups.values()) {
    const ordered = group
      .map(item => item.declaration)
      .filter((value, index, array) => array.findIndex(candidate => candidate.id === value.id) === index)
      .sort((left, right) => left.sourceOrder - right.sourceOrder || left.lineNumber - right.lineNumber);

    const targetKind = group[0]?.targetKind;
    const targetName = group[0]?.targetName;
    if (!targetKind || !targetName)
      continue;

    for (let leftIndex = 0; leftIndex < ordered.length; leftIndex++) {
      for (let rightIndex = leftIndex + 1; rightIndex < ordered.length; rightIndex++) {
        const left = ordered[leftIndex];
        const right = ordered[rightIndex];
        const priority = compareCascadePriority(right, left);
        if (priority === 0)
          continue;

        const winner = priority > 0 ? right : left;
        const loser = priority > 0 ? left : right;
        const edgeKey = `${winner.id}|${loser.id}|${targetKind}|${targetName}|${winner.propertyName}`;
        if (emitted.has(edgeKey))
          continue;

        emitted.add(edgeKey);
        edges.push({
          sourceId: winner.id,
          targetId: loser.id,
          type: 'Overrides',
          callSite: `${relativePath}:${winner.lineNumber}`,
          properties: {
            relationshipKind: 'LikelyCssCascadeOverride',
            inferenceConfidence: 'inferred',
            propertyName: winner.propertyName,
            sharedTargetKind: targetKind,
            sharedTargetName: targetName,
            reason: buildOverrideReason(winner, loser),
            winnerSpecificity: winner.specificity.display,
            loserSpecificity: loser.specificity.display,
            winnerSourceOrder: String(winner.sourceOrder),
            loserSourceOrder: String(loser.sourceOrder),
          },
        });
      }
    }
  }
}

function compareCascadePriority(left: IndexedStyleDeclaration, right: IndexedStyleDeclaration): number {
  const specificityComparison = compareSpecificity(left.specificity, right.specificity);
  if (specificityComparison !== 0)
    return specificityComparison;

  if (left.sourceOrder !== right.sourceOrder)
    return left.sourceOrder - right.sourceOrder;

  return left.lineNumber - right.lineNumber;
}

function compareSpecificity(left: SelectorSpecificity, right: SelectorSpecificity): number {
  if (left.ids !== right.ids)
    return left.ids - right.ids;

  if (left.classes !== right.classes)
    return left.classes - right.classes;

  return left.elements - right.elements;
}

function buildOverrideReason(winner: IndexedStyleDeclaration, loser: IndexedStyleDeclaration): string {
  const specificityComparison = compareSpecificity(winner.specificity, loser.specificity);
  if (specificityComparison !== 0) {
    return `higher specificity ${winner.specificity.display} over ${loser.specificity.display}`;
  }

  return `same specificity ${winner.specificity.display}; later source order ${winner.sourceOrder} over ${loser.sourceOrder}`;
}

function computeSelectorSpecificity(selectorText: string): SelectorSpecificity {
  const withoutWhere = selectorText.replace(WHERE_PSEUDO_SELECTOR, '');
  const ids = countMatches(withoutWhere, ID_SELECTOR);
  const classes = countMatches(withoutWhere, CLASS_SELECTOR)
    + countMatches(withoutWhere, ATTRIBUTE_SELECTOR)
    + countMatches(withoutWhere, PSEUDO_CLASS_SELECTOR);
  const withoutNonElementTokens = withoutWhere
    .replace(ID_SELECTOR, ' ')
    .replace(CLASS_SELECTOR, ' ')
    .replace(ATTRIBUTE_SELECTOR, ' ')
    .replace(PSEUDO_CLASS_SELECTOR, ' ')
    .replace(/&/g, ' ');
  const elements = countMatches(withoutNonElementTokens, PSEUDO_ELEMENT_SELECTOR)
    + countMatches(withoutNonElementTokens, ELEMENT_SELECTOR, 2);

  return {
    ids,
    classes,
    elements,
    score: (ids * 100) + (classes * 10) + elements,
    display: `${ids},${classes},${elements}`,
  };
}

function countMatches(value: string, pattern: RegExp, groupIndex = 0): number {
  pattern.lastIndex = 0;
  let count = 0;
  let match: RegExpExecArray | null;
  while ((match = pattern.exec(value)) !== null) {
    if (groupIndex === 0 || match[groupIndex])
      count++;
  }

  return count;
}
