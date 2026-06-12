import path from 'node:path';
import type { CallExpression, Node, SourceFile } from 'ts-morph';
import { SyntaxKind } from 'ts-morph';
import type { CodeEdgeDto, CodeNodeDto } from '../types.js';
import { addNode, fileId, nodeId, resolveStringLiteralValue } from './common.js';

interface HttpRouteCall {
  filePath: string;
  lineNumber: number;
  method: string;
  routeTemplate: string;
  normalizedRoute: string;
  confidence: number;
  source: string;
  sourceId: string;
  isAsync?: boolean;
}

interface RouteResolution {
  routeTemplate: string;
  confidence: number;
  source: string;
}

interface RouteContext {
  stringConstants: Map<string, string>;
  objectRoutes: Map<string, Map<string, string>>;
}

export function collectRouteNodes(
  sourceFile: SourceFile,
  rootPath: string,
  projectName: string,
  nodes: CodeNodeDto[],
  knownIds: Set<string>,
): void {
  const routes = extractHttpRouteCalls(sourceFile, rootPath, projectName, knownIds);
  for (const route of routes) {
    addNode(nodes, knownIds, {
      id: buildApiEndpointId(projectName, route.method, route.normalizedRoute),
      name: `${route.method} ${route.normalizedRoute}`,
      type: 'ApiEndpoint',
      summary: `Route endpoint (${route.source}) for \`${route.method} ${route.routeTemplate}\``,
      projectContext: projectName,
    });
  }
}

export function collectRouteEdges(
  sourceFile: SourceFile,
  rootPath: string,
  projectName: string,
  edges: CodeEdgeDto[],
  knownIds: Set<string>,
): void {
  const routes = extractHttpRouteCalls(sourceFile, rootPath, projectName, knownIds);
  for (const route of routes) {
    edges.push({
      sourceId: route.sourceId,
      targetId: buildApiEndpointId(projectName, route.method, route.normalizedRoute),
      type: 'Calls',
      isAsync: route.isAsync,
      callSite: `${route.filePath}:${route.lineNumber}`,
      confidence: route.confidence,
    });
  }
}

function extractHttpRouteCalls(
  sourceFile: SourceFile,
  rootPath: string,
  projectName: string,
  knownIds: Set<string>,
): HttpRouteCall[] {
  const relPath = path.relative(rootPath, sourceFile.getFilePath()).replace(/\\/g, '/');
  const context = buildRouteContext(sourceFile);
  const results: HttpRouteCall[] = [];

  for (const call of sourceFile.getDescendantsOfKind(SyntaxKind.CallExpression)) {
    const descriptor = resolveHttpRouteCall(call, relPath, projectName, knownIds, context);
    if (!descriptor) continue;
    results.push(descriptor);
  }

  return dedupeRoutes(results);
}

function resolveHttpRouteCall(
  call: CallExpression,
  relPath: string,
  projectName: string,
  knownIds: Set<string>,
  context: RouteContext,
): HttpRouteCall | undefined {
  const expression = call.getExpression();
  const lineNumber = call.getStartLineNumber();
  const sourceId = resolveRouteSourceId(call, projectName, relPath, knownIds);
  const isAsync = !!call.getFirstAncestorByKind(SyntaxKind.AwaitExpression);

  if (expression.getKind() === SyntaxKind.Identifier && expression.getText() === 'fetch') {
    const routeArg = call.getArguments()[0];
    if (!routeArg) return undefined;

    const resolved = resolveRouteExpression(routeArg, context);
    if (!resolved) return undefined;

    const method = resolveFetchMethod(call) ?? 'GET';
    return {
      filePath: relPath,
      lineNumber,
      method,
      routeTemplate: resolved.routeTemplate,
      normalizedRoute: normalizeRouteTemplate(resolved.routeTemplate),
      confidence: resolved.confidence,
      source: 'fetch',
      sourceId,
      isAsync,
    };
  }

  if (expression.getKind() === SyntaxKind.PropertyAccessExpression) {
    const propertyAccess = expression.asKindOrThrow(SyntaxKind.PropertyAccessExpression);
    const methodName = propertyAccess.getName().toLowerCase();

    if (['get', 'post', 'put', 'patch', 'delete'].includes(methodName)) {
      const routeArg = call.getArguments()[0];
      if (!routeArg) return undefined;

      const resolved = resolveRouteExpression(routeArg, context);
      if (!resolved) return undefined;

      return {
        filePath: relPath,
        lineNumber,
        method: methodName.toUpperCase(),
        routeTemplate: resolved.routeTemplate,
        normalizedRoute: normalizeRouteTemplate(resolved.routeTemplate),
        confidence: resolved.confidence,
        source: propertyAccess.getExpression().getText() === 'axios' ? 'axios' : 'http-client-wrapper',
        sourceId,
        isAsync,
      };
    }

    if (methodName === 'request') {
      const configArg = call.getArguments()[0];
      const request = resolveRequestConfig(configArg, context);
      if (!request) return undefined;

      return {
        filePath: relPath,
        lineNumber,
        method: request.method,
        routeTemplate: request.routeTemplate,
        normalizedRoute: normalizeRouteTemplate(request.routeTemplate),
        confidence: request.confidence,
        source: propertyAccess.getExpression().getText() === 'axios' ? 'axios' : 'http-client-wrapper',
        sourceId,
        isAsync,
      };
    }
  }

  return undefined;
}

function resolveFetchMethod(call: CallExpression): string | undefined {
  const initArg = call.getArguments()[1];
  if (!initArg || initArg.getKind() !== SyntaxKind.ObjectLiteralExpression) return undefined;

  const objectLiteral = initArg.asKindOrThrow(SyntaxKind.ObjectLiteralExpression);
  const methodProperty = objectLiteral.getProperty('method');
  if (!methodProperty || methodProperty.getKind() !== SyntaxKind.PropertyAssignment) return undefined;

  const initializer = methodProperty.asKindOrThrow(SyntaxKind.PropertyAssignment).getInitializer();
  if (!initializer) return undefined;

  return resolveStringLiteralValue(initializer)?.toUpperCase();
}

function resolveRequestConfig(
  configArg: Node | undefined,
  context: RouteContext,
): RouteResolution & { method: string } | undefined {
  if (!configArg || configArg.getKind() !== SyntaxKind.ObjectLiteralExpression) {
    return undefined;
  }

  const objectLiteral = configArg.asKindOrThrow(SyntaxKind.ObjectLiteralExpression);
  const urlProperty = objectLiteral.getProperty('url');
  const methodProperty = objectLiteral.getProperty('method');
  if (!urlProperty || !methodProperty) return undefined;
  if (urlProperty.getKind() !== SyntaxKind.PropertyAssignment || methodProperty.getKind() !== SyntaxKind.PropertyAssignment) {
    return undefined;
  }

  const urlInitializer = urlProperty.asKindOrThrow(SyntaxKind.PropertyAssignment).getInitializer();
  const methodInitializer = methodProperty.asKindOrThrow(SyntaxKind.PropertyAssignment).getInitializer();
  if (!urlInitializer || !methodInitializer) return undefined;

  const resolvedRoute = resolveRouteExpression(urlInitializer, context);
  const method = resolveStringLiteralValue(methodInitializer)?.toUpperCase();
  if (!resolvedRoute || !method) return undefined;

  return { ...resolvedRoute, method };
}

function buildRouteContext(sourceFile: SourceFile): RouteContext {
  const stringConstants = new Map<string, string>();
  const objectRoutes = new Map<string, Map<string, string>>();

  for (const variable of sourceFile.getVariableDeclarations()) {
    const initializer = variable.getInitializer();
    if (!initializer) continue;

    const route = resolveRouteLiteral(initializer);
    if (route) {
      stringConstants.set(variable.getName(), route);
      continue;
    }

    if (initializer.getKind() !== SyntaxKind.ObjectLiteralExpression) continue;

    const routes = new Map<string, string>();
    for (const property of initializer.asKindOrThrow(SyntaxKind.ObjectLiteralExpression).getProperties()) {
      if (property.getKind() !== SyntaxKind.PropertyAssignment) continue;

      const assignment = property.asKindOrThrow(SyntaxKind.PropertyAssignment);
      const name = assignment.getName();
      const value = assignment.getInitializer();
      if (!value) continue;

      const resolved = resolveRouteLiteral(value);
      if (resolved) routes.set(name, resolved);
    }

    if (routes.size > 0) objectRoutes.set(variable.getName(), routes);
  }

  return { stringConstants, objectRoutes };
}

function resolveRouteExpression(node: Node, context: RouteContext): RouteResolution | undefined {
  const literal = resolveRouteLiteral(node);
  if (literal) {
    const confidence = node.getKind() === SyntaxKind.TemplateExpression ? 0.9 : 1.0;
    return {
      routeTemplate: literal,
      confidence,
      source: node.getKind() === SyntaxKind.TemplateExpression ? 'template' : 'literal',
    };
  }

  if (node.getKind() === SyntaxKind.Identifier) {
    const value = context.stringConstants.get(node.getText());
    if (value) {
      return {
        routeTemplate: value,
        confidence: 0.95,
        source: 'constant',
      };
    }
  }

  if (node.getKind() === SyntaxKind.PropertyAccessExpression) {
    const propertyAccess = node.asKindOrThrow(SyntaxKind.PropertyAccessExpression);
    const owner = propertyAccess.getExpression().getText();
    const property = propertyAccess.getName();
    const value = context.objectRoutes.get(owner)?.get(property);
    if (value) {
      return {
        routeTemplate: value,
        confidence: 0.95,
        source: 'constant',
      };
    }
  }

  return undefined;
}

function resolveRouteLiteral(node: Node): string | undefined {
  const stringLiteral = resolveStringLiteralValue(node);
  if (stringLiteral && looksLikeRoute(stringLiteral)) return stringLiteral;

  if (node.getKind() === SyntaxKind.TemplateExpression) {
    const template = node.asKindOrThrow(SyntaxKind.TemplateExpression);
    const result = [
      template.getHead().getLiteralText(),
      ...template.getTemplateSpans().flatMap(span => ['{param}', span.getLiteral().getLiteralText()]),
    ].join('');

    return looksLikeRoute(result) ? result : undefined;
  }

  if (node.getKind() === SyntaxKind.NoSubstitutionTemplateLiteral) {
    const text = node.asKindOrThrow(SyntaxKind.NoSubstitutionTemplateLiteral).getLiteralText();
    return looksLikeRoute(text) ? text : undefined;
  }

  return undefined;
}

function looksLikeRoute(value: string): boolean {
  return value.startsWith('/') || value.startsWith('http://') || value.startsWith('https://');
}

function normalizeRouteTemplate(template: string): string {
  let normalized = template.trim();
  normalized = normalized.replace(/^https?:\/\/[^/]+/i, '');
  normalized = normalized.split(/[?#]/, 1)[0] ?? normalized;
  normalized = normalized.replace(/\\/g, '/');
  normalized = normalized.replace(/\/{2,}/g, '/');
  normalized = normalized.replace(/:[A-Za-z_][A-Za-z0-9_]*/g, '{param}');
  normalized = normalized.replace(/\{[^}]+\}/g, '{param}');

  if (!normalized.startsWith('/')) {
    normalized = `/${normalized}`;
  }

  normalized = normalized.endsWith('/') && normalized.length > 1
    ? normalized.slice(0, -1)
    : normalized;

  return normalized.toLowerCase();
}

function buildApiEndpointId(projectName: string, method: string, normalizedRoute: string): string {
  return `${projectName}::ApiEndpoint::${method} ${normalizedRoute}`;
}

function resolveRouteSourceId(
  call: CallExpression,
  projectName: string,
  relPath: string,
  knownIds: Set<string>,
): string {
  const enclosingMethod = call.getFirstAncestorByKind(SyntaxKind.MethodDeclaration);
  if (enclosingMethod) {
    const classDecl = enclosingMethod.getFirstAncestorByKind(SyntaxKind.ClassDeclaration);
    if (classDecl?.getName()) {
      const candidateId = nodeId(projectName, relPath, `${classDecl.getName()}.${enclosingMethod.getName()}`, 'Method');
      if (knownIds.has(candidateId)) return candidateId;
    }
  }

  const enclosingFunction = call.getFirstAncestorByKind(SyntaxKind.FunctionDeclaration);
  if (enclosingFunction?.getName()) {
    const candidateId = nodeId(projectName, relPath, enclosingFunction.getName()!, 'Method');
    if (knownIds.has(candidateId)) return candidateId;
  }

  return fileId(projectName, relPath);
}

function dedupeRoutes(routes: HttpRouteCall[]): HttpRouteCall[] {
  const seen = new Set<string>();
  const deduped: HttpRouteCall[] = [];
  for (const route of routes) {
    const key = `${route.sourceId}|${route.method}|${route.normalizedRoute}|${route.lineNumber}`;
    if (seen.has(key)) continue;
    seen.add(key);
    deduped.push(route);
  }
  return deduped;
}
