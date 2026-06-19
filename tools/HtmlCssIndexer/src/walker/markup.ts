import fs from 'node:fs';
import path from 'node:path';
import { Node, Project, SyntaxKind, type JsxAttribute } from 'ts-morph';
import type { CodeEdgeDto, CodeNodeDto } from '@codemeridian/indexer-shared';
import { addNode, fileNodeId, frontendConceptId, hashText, lineCountFromContent, lineNumberAt, splitClassNames, toRelativePath } from './common.js';

const HTML_CLASS_ATTR = /\bclass\s*=\s*"([^"]+)"/gi;
const HTML_ID_ATTR = /\bid\s*=\s*"([^"]+)"/gi;
const HTML_STYLESHEET_LINK = /<link\b[^>]*rel\s*=\s*"stylesheet"[^>]*href\s*=\s*"([^"]+)"/gi;

export function collectHtmlArtifacts(
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
  const fileId = addFrontendFileNode(rootPath, projectName, relativePath, content, nodes, knownIds, resolveFileRole, 'MarkupFile');

  collectHtmlClassAndIdUsage(content, relativePath, fileId, projectName, nodes, edges, knownIds);
  collectHtmlStylesheetLinks(rootPath, filePath, relativePath, fileId, projectName, nodes, edges, knownIds, resolveFileRole, content);
}

export function collectTsxArtifacts(
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
  const fileId = addFrontendFileNode(rootPath, projectName, relativePath, content, nodes, knownIds, resolveFileRole, 'ComponentFile');

  const project = new Project({
    useInMemoryFileSystem: true,
    compilerOptions: {
      allowJs: true,
      jsx: 2,
    },
  });
  const sourceFile = project.createSourceFile(relativePath, content, { overwrite: true });

  for (const attribute of sourceFile.getDescendantsOfKind(SyntaxKind.JsxAttribute)) {
    const attributeName = attribute.getNameNode().getText();
    if (attributeName === 'className') {
      collectJsxClassUsage(attribute, relativePath, fileId, projectName, nodes, edges, knownIds);
    }
    if (attributeName === 'id') {
      collectJsxIdUsage(attribute, relativePath, fileId, projectName, nodes, edges, knownIds);
    }
  }

  for (const importDeclaration of sourceFile.getImportDeclarations()) {
    const target = importDeclaration.getModuleSpecifierValue();
    if (!/\.(css|scss)$/i.test(target))
      continue;

    const importedPath = resolveImportedPath(path.dirname(filePath), target);
    if (!importedPath)
      continue;

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
      callSite: `${relativePath}:${importDeclaration.getStartLineNumber()}`,
    });
  }
}

function addFrontendFileNode(
  rootPath: string,
  projectName: string,
  relativePath: string,
  content: string,
  nodes: CodeNodeDto[],
  knownIds: Set<string>,
  resolveFileRole: ((relativePath: string) => string | undefined) | undefined,
  frontendRole: 'MarkupFile' | 'ComponentFile',
): string {
  const id = fileNodeId(projectName, relativePath);
  addNode(nodes, knownIds, {
    id,
    name: path.posix.basename(relativePath),
    type: 'File',
    filePath: relativePath,
    lineNumber: 1,
    lineCount: lineCountFromContent(content),
    summary: 'HTML/CSS/SCSS indexer frontend source file.',
    sourceHash: hashText(content),
    projectContext: projectName,
    properties: {
      frontendRole,
      rootPath: rootPath.replace(/\\/g, '/'),
    },
  }, resolveFileRole);

  return id;
}

function collectHtmlClassAndIdUsage(
  content: string,
  relativePath: string,
  fileId: string,
  projectName: string,
  nodes: CodeNodeDto[],
  edges: CodeEdgeDto[],
  knownIds: Set<string>,
): void {
  let match: RegExpExecArray | null;

  while ((match = HTML_CLASS_ATTR.exec(content)) !== null) {
    for (const className of splitClassNames(match[1])) {
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
        sourceId: fileId,
        targetId: classId,
        type: 'UsesClass',
        callSite: `${relativePath}:${lineNumberAt(content, match.index)}`,
      });
    }
  }

  while ((match = HTML_ID_ATTR.exec(content)) !== null) {
    const idValue = match[1]?.trim();
    if (!idValue)
      continue;

    const conceptId = frontendConceptId(projectName, 'CssId', idValue);
    addNode(nodes, knownIds, {
      id: conceptId,
      name: idValue,
      type: 'ExternalConcept',
      projectContext: projectName,
      properties: {
        externalKind: 'CssId',
      },
    });

    edges.push({
      sourceId: fileId,
      targetId: conceptId,
      type: 'UsesId',
      callSite: `${relativePath}:${lineNumberAt(content, match.index)}`,
    });
  }
}

function collectHtmlStylesheetLinks(
  rootPath: string,
  filePath: string,
  relativePath: string,
  fileId: string,
  projectName: string,
  nodes: CodeNodeDto[],
  edges: CodeEdgeDto[],
  knownIds: Set<string>,
  resolveFileRole: ((relativePath: string) => string | undefined) | undefined,
  content: string,
): void {
  let match: RegExpExecArray | null;

  while ((match = HTML_STYLESHEET_LINK.exec(content)) !== null) {
    const importedPath = resolveImportedPath(path.dirname(filePath), match[1]);
    if (!importedPath)
      continue;

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
      callSite: `${relativePath}:${lineNumberAt(content, match.index)}`,
    });
  }
}

function collectJsxClassUsage(
  attribute: JsxAttribute,
  relativePath: string,
  fileId: string,
  projectName: string,
  nodes: CodeNodeDto[],
  edges: CodeEdgeDto[],
  knownIds: Set<string>,
): void {
  for (const className of resolveStaticClassNames(attribute)) {
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
      sourceId: fileId,
      targetId: classId,
      type: 'UsesClass',
      callSite: `${relativePath}:${attribute.getStartLineNumber()}`,
    });
  }
}

function collectJsxIdUsage(
  attribute: JsxAttribute,
  relativePath: string,
  fileId: string,
  projectName: string,
  nodes: CodeNodeDto[],
  edges: CodeEdgeDto[],
  knownIds: Set<string>,
): void {
  for (const idValue of resolveStaticClassNames(attribute)) {
    const conceptId = frontendConceptId(projectName, 'CssId', idValue);
    addNode(nodes, knownIds, {
      id: conceptId,
      name: idValue,
      type: 'ExternalConcept',
      projectContext: projectName,
      properties: {
        externalKind: 'CssId',
      },
    });

    edges.push({
      sourceId: fileId,
      targetId: conceptId,
      type: 'UsesId',
      callSite: `${relativePath}:${attribute.getStartLineNumber()}`,
    });
  }
}

function resolveStaticClassNames(attribute: JsxAttribute): string[] {
  const initializer = attribute.getInitializer();
  if (!initializer)
    return [];

  if (initializer.getKind() === SyntaxKind.StringLiteral) {
    return splitClassNames(initializer.asKindOrThrow(SyntaxKind.StringLiteral).getLiteralValue());
  }

  if (initializer.getKind() !== SyntaxKind.JsxExpression)
    return [];

  const expression = initializer.asKindOrThrow(SyntaxKind.JsxExpression).getExpression();
  if (!expression)
    return [];

  const tokens: string[] = [];

  if (expression.getKind() === SyntaxKind.StringLiteral) {
    tokens.push(expression.asKindOrThrow(SyntaxKind.StringLiteral).getLiteralValue());
  } else if (expression.getKind() === SyntaxKind.NoSubstitutionTemplateLiteral) {
    tokens.push(expression.asKindOrThrow(SyntaxKind.NoSubstitutionTemplateLiteral).getLiteralText());
  } else if (expression.getKind() === SyntaxKind.TemplateExpression) {
    const templateExpression = expression.asKindOrThrow(SyntaxKind.TemplateExpression);
    tokens.push(templateExpression.getHead().getLiteralText());
    for (const span of templateExpression.getTemplateSpans()) {
      const inner = span.getExpression();
      if (Node.isStringLiteral(inner))
        tokens.push(inner.getLiteralValue());
      tokens.push(span.getLiteral().getLiteralText());
    }
  }

  return splitClassNames(tokens.join(' '));
}

function resolveImportedPath(baseDirectory: string, target: string): string | undefined {
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
