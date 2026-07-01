import path from 'node:path';
import type {
  BinaryExpression,
  ElementAccessExpression,
  Node,
  PropertyAccessExpression,
  SourceFile,
  VariableDeclaration,
} from 'ts-morph';
import { SyntaxKind } from 'ts-morph';
import type { CodeEdgeDto, CodeNodeDto } from '../types.js';
import { addNode, fileId, nodeId } from './common.js';

export function collectConfigurationNodes(
  sourceFile: SourceFile,
  _rootPath: string,
  projectName: string,
  nodes: CodeNodeDto[],
  knownIds: Set<string>,
  classifyFileRole?: (relativePath: string) => string | undefined,
): void {
  for (const usage of findConfigurationUsages(sourceFile)) {
    const canonicalKey = normalizeConfigurationKey(usage.rawKey);
    addNode(nodes, knownIds, {
      id: `${projectName}::ConfigurationKey::${canonicalKey}`,
      name: canonicalKey,
      type: 'ConfigurationKey',
      projectContext: projectName,
      properties: {
        canonicalKey,
        normalizedKey: canonicalKey.toLowerCase(),
        isSecretLike: isSecretLike(canonicalKey) ? 'true' : 'false',
      },
    }, classifyFileRole);
  }
}

export function collectConfigurationEdges(
  sourceFile: SourceFile,
  rootPath: string,
  projectName: string,
  edges: CodeEdgeDto[],
): void {
  const relPath = path.relative(rootPath, sourceFile.getFilePath()).replace(/\\/g, '/');

  for (const usage of findConfigurationUsages(sourceFile)) {
    const canonicalKey = normalizeConfigurationKey(usage.rawKey);
    edges.push({
      sourceId: resolveSourceId(usage.node, projectName, relPath),
      targetId: `${projectName}::ConfigurationKey::${canonicalKey}`,
      type: usage.relationshipType,
      callSite: `${relPath}:${usage.node.getStartLineNumber()}`,
      confidence: usage.relationshipType === 'ReadsConfig' ? 0.95 : 0.9,
      properties: {
        rawKey: usage.rawKey,
        accessPattern: usage.accessPattern,
        ...(usage.optionsType ? { optionsType: usage.optionsType } : {}),
      },
    });
  }
}

interface ConfigurationUsageCandidate {
  node: Node;
  rawKey: string;
  accessPattern: string;
  relationshipType: 'ReadsConfig' | 'BindsConfig';
  optionsType?: string;
}

function findConfigurationUsages(sourceFile: SourceFile): ConfigurationUsageCandidate[] {
  const results: ConfigurationUsageCandidate[] = [];

  for (const propertyAccess of sourceFile.getDescendantsOfKind(SyntaxKind.PropertyAccessExpression)) {
    const rawKey = readEnvironmentPropertyAccess(propertyAccess);
    if (!rawKey) continue;

    results.push({
      node: propertyAccess,
      rawKey,
      accessPattern: propertyAccess.getExpression().getText() === 'process.env' ? 'process.env' : 'import.meta.env',
      relationshipType: 'ReadsConfig',
    });
  }

  for (const elementAccess of sourceFile.getDescendantsOfKind(SyntaxKind.ElementAccessExpression)) {
    const rawKey = readEnvironmentElementAccess(elementAccess);
    if (!rawKey) continue;

    results.push({
      node: elementAccess,
      rawKey,
      accessPattern: elementAccess.getExpression().getText() === 'process.env' ? 'process.env[indexer]' : 'import.meta.env[indexer]',
      relationshipType: 'ReadsConfig',
    });
  }

  for (const declaration of sourceFile.getDescendantsOfKind(SyntaxKind.VariableDeclaration)) {
    const destructured = readEnvironmentDestructuring(declaration);
    results.push(...destructured);
  }

  for (const binary of sourceFile.getDescendantsOfKind(SyntaxKind.BinaryExpression)) {
    const bound = readEnvironmentSchemaBinding(binary);
    if (bound) results.push(bound);
  }

  return dedupeUsages(results);
}

function readEnvironmentPropertyAccess(propertyAccess: PropertyAccessExpression): string | undefined {
  if (propertyAccess.getParentIfKind(SyntaxKind.PropertyAccessExpression)) {
    return undefined;
  }

  const expressionText = propertyAccess.getExpression().getText();
  if (expressionText !== 'process.env' && expressionText !== 'import.meta.env') {
    return undefined;
  }

  return propertyAccess.getName();
}

function readEnvironmentElementAccess(elementAccess: ElementAccessExpression): string | undefined {
  const expressionText = elementAccess.getExpression().getText();
  if (expressionText !== 'process.env' && expressionText !== 'import.meta.env') {
    return undefined;
  }

  const argument = elementAccess.getArgumentExpression();
  if (!argument) return undefined;

  return readStringLiteral(argument);
}

function readEnvironmentDestructuring(declaration: VariableDeclaration): ConfigurationUsageCandidate[] {
  const bindingPattern = declaration.getNameNode().asKind(SyntaxKind.ObjectBindingPattern);
  if (!bindingPattern) return [];

  const initializer = declaration.getInitializer();
  if (!initializer) return [];

  const sourceText = initializer.getText();
  if (sourceText !== 'process.env' && sourceText !== 'import.meta.env') return [];

  return bindingPattern.getElements()
    .map(element => {
      const propertyNameNode = element.getPropertyNameNode();
      const rawKey = propertyNameNode?.getText() ?? element.getName();
      return {
        node: element,
        rawKey,
        accessPattern: sourceText === 'process.env' ? 'process.env destructure' : 'import.meta.env destructure',
        relationshipType: 'ReadsConfig' as const,
      };
    });
}

function readEnvironmentSchemaBinding(binary: BinaryExpression): ConfigurationUsageCandidate | undefined {
  if (binary.getOperatorToken().getKind() !== SyntaxKind.EqualsToken) return undefined;

  const leftText = binary.getLeft().getText();
  if (!leftText.endsWith('.env') && leftText !== 'env') return undefined;

  const right = binary.getRight();
  if (!right.isKind(SyntaxKind.CallExpression)) return undefined;

  const callee = right.getExpression().getText();
  if (!callee.endsWith('.object') && !callee.endsWith('.pick')) return undefined;

  const objectLiteral = right.getArguments()[0]?.asKind(SyntaxKind.ObjectLiteralExpression);
  if (!objectLiteral) return undefined;

  const firstProperty = objectLiteral.getProperties().find(property => property.isKind(SyntaxKind.PropertyAssignment));
  const key = firstProperty?.asKind(SyntaxKind.PropertyAssignment)?.getName();
  if (!key) return undefined;

  return {
    node: binary,
    rawKey: key,
    accessPattern: 'env schema',
    relationshipType: 'BindsConfig',
    optionsType: deriveOptionsType(leftText),
  };
}

function deriveOptionsType(leftText: string): string | undefined {
  const candidate = leftText.split('.').slice(0, -1).join('.');
  return candidate.length > 0 ? candidate : undefined;
}

function resolveSourceId(node: Node, projectName: string, relPath: string): string {
  const classDecl = node.getFirstAncestorByKind(SyntaxKind.ClassDeclaration);
  const methodDecl = node.getFirstAncestorByKind(SyntaxKind.MethodDeclaration);
  if (classDecl && methodDecl) {
    return nodeId(projectName, relPath, `${classDecl.getName() ?? '<anonymous>'}.${methodDecl.getName()}`, 'Method');
  }

  const functionDecl = node.getFirstAncestorByKind(SyntaxKind.FunctionDeclaration);
  if (functionDecl?.getName()) {
    return nodeId(projectName, relPath, functionDecl.getName()!, 'Method');
  }

  if (classDecl?.getName()) {
    return nodeId(projectName, relPath, classDecl.getName()!, 'Class');
  }

  return fileId(projectName, relPath);
}

function normalizeConfigurationKey(rawKey: string): string {
  return rawKey.trim().replace(/__/g, ':').replace(/^['"]|['"]$/g, '');
}

function isSecretLike(key: string): boolean {
  const lowered = key.toLowerCase();
  return lowered.includes('password') ||
    lowered.includes('secret') ||
    lowered.includes('token') ||
    lowered.includes('apikey') ||
    lowered.includes('api_key');
}

function readStringLiteral(node: Node): string | undefined {
  if (node.isKind(SyntaxKind.StringLiteral)) {
    return node.getLiteralValue();
  }

  if (node.isKind(SyntaxKind.NoSubstitutionTemplateLiteral)) {
    return node.getLiteralText();
  }

  return undefined;
}

function dedupeUsages(usages: ConfigurationUsageCandidate[]): ConfigurationUsageCandidate[] {
  const seen = new Set<string>();
  const deduped: ConfigurationUsageCandidate[] = [];

  for (const usage of usages) {
    const key = [
      usage.relationshipType,
      usage.rawKey,
      usage.accessPattern,
      usage.optionsType ?? '',
      usage.node.getStartLineNumber(),
      usage.node.getSourceFile().getFilePath(),
    ].join('|');

    if (seen.has(key)) continue;
    seen.add(key);
    deduped.push(usage);
  }

  return deduped;
}

export const __testing = {
  normalizeConfigurationKey,
  isSecretLike,
};
