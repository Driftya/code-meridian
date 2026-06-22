import { createHash } from 'node:crypto';
import path from 'node:path';
import { SyntaxKind } from 'ts-morph';
import { addNode, fileId, nodeId, resolveStringLiteralValue } from './common.js';
const sqlTableRegex = /\b(?:FROM|JOIN|UPDATE|INTO|MERGE\s+INTO|DELETE\s+FROM|TRUNCATE\s+TABLE)\s+([#\[\]"`A-Za-z0-9_.]+)/gi;
const sqlStatementRegex = /^\s*(SELECT|WITH|INSERT|UPDATE|DELETE|MERGE|TRUNCATE|CREATE|ALTER|DROP)\b/i;
const cypherLabelRegex = /\([^\)]*:(?<name>[A-Za-z_][A-Za-z0-9_]*)/g;
const cypherRelationshipRegex = /\[[^\]]*:(?<name>[A-Za-z_][A-Za-z0-9_]*)/g;
const cypherReadRegex = /^\s*(MATCH|OPTIONAL\s+MATCH|CALL|RETURN|UNWIND|WITH|LOAD\s+CSV|SHOW|PROFILE|EXPLAIN)\b/i;
const cypherWriteRegex = /^\s*(CREATE|MERGE|DELETE|DETACH\s+DELETE|SET|REMOVE)\b/i;
export function collectDatabaseTracingNodes(sourceFile, rootPath, projectName, nodes, knownIds, options, classifyFileRole) {
    const relPath = path.relative(rootPath, sourceFile.getFilePath()).replace(/\\/g, '/');
    for (const usage of findDatabaseUsages(sourceFile, projectName, relPath, options)) {
        const operationNodeId = buildOperationNodeId(projectName, usage.sourceId, relPath, usage.lineNumber, usage);
        addNode(nodes, knownIds, {
            id: operationNodeId,
            name: `${usage.provider} ${usage.operationType} ${usage.tables[0]}`,
            type: 'ExternalConcept',
            filePath: relPath,
            lineNumber: usage.lineNumber,
            projectContext: projectName,
            properties: {
                externalKind: 'DatabaseOperation',
                provider: usage.provider,
                operationType: usage.operationType,
                recognizerId: usage.recognizerId,
                methodName: usage.methodName,
            },
        }, classifyFileRole);
        for (const tableName of usage.tables) {
            addNode(nodes, knownIds, {
                id: `${projectName}::DatabaseTable::${tableName}`,
                name: tableName,
                type: 'DatabaseTable',
                projectContext: projectName,
                properties: {
                    externalKind: 'DatabaseTable',
                    normalizedName: tableName.toLowerCase(),
                },
            }, classifyFileRole);
        }
    }
}
export function collectDatabaseTracingEdges(sourceFile, rootPath, projectName, edges, options) {
    const relPath = path.relative(rootPath, sourceFile.getFilePath()).replace(/\\/g, '/');
    for (const usage of findDatabaseUsages(sourceFile, projectName, relPath, options)) {
        const operationNodeId = buildOperationNodeId(projectName, usage.sourceId, relPath, usage.lineNumber, usage);
        edges.push({
            sourceId: usage.sourceId,
            targetId: operationNodeId,
            type: usage.operationType,
            callSite: `${relPath}:${usage.lineNumber}`,
            confidence: 0.88,
            properties: {
                provider: usage.provider,
                recognizerId: usage.recognizerId,
            },
        });
        for (const tableName of usage.tables) {
            edges.push({
                sourceId: operationNodeId,
                targetId: `${projectName}::DatabaseTable::${tableName}`,
                type: usage.operationType,
                callSite: `${relPath}:${usage.lineNumber}`,
                confidence: 0.9,
                properties: {
                    provider: usage.provider,
                    recognizerId: usage.recognizerId,
                    tableName,
                },
            });
        }
    }
}
function findDatabaseUsages(sourceFile, projectName, relPath, options) {
    if (!options.enabled || options.presets.length === 0) {
        return [];
    }
    const presets = options.presets.filter(preset => preset.enabled &&
        preset.id.length > 0 &&
        preset.provider.length > 0 &&
        supportsLanguage(preset, 'TypeScript'));
    if (presets.length === 0) {
        return [];
    }
    const importedModules = new Set(sourceFile.getImportDeclarations().map(decl => decl.getModuleSpecifierValue()));
    const usages = [];
    for (const call of sourceFile.getDescendantsOfKind(SyntaxKind.CallExpression)) {
        for (const preset of presets) {
            const usage = tryRecognizeUsage(call, preset, projectName, relPath, options.maxTablesPerOperation, importedModules);
            if (!usage) {
                continue;
            }
            usages.push(usage);
            break;
        }
    }
    return dedupeUsages(usages);
}
function tryRecognizeUsage(call, preset, projectName, relPath, maxTablesPerOperation, importedModules) {
    const methodName = getMemberName(call);
    if (!methodName) {
        return undefined;
    }
    return preset.strategy.trim().toLowerCase() === 'prisma'
        ? tryRecognizePrisma(call, preset, projectName, relPath, methodName, maxTablesPerOperation, importedModules)
        : preset.strategy.trim().toLowerCase() === 'knex'
            ? tryRecognizeKnex(call, preset, projectName, relPath, methodName, maxTablesPerOperation, importedModules)
            : preset.strategy.trim().toLowerCase() === 'cypher'
                ? tryRecognizeCypher(call, preset, projectName, relPath, methodName, maxTablesPerOperation, importedModules)
                : undefined;
}
function tryRecognizePrisma(call, preset, projectName, relPath, methodName, maxTablesPerOperation, importedModules) {
    const operationType = getOperationType(methodName, preset);
    if (!operationType) {
        return undefined;
    }
    const receiver = getReceiver(call);
    const receiverText = receiver?.getText() ?? '';
    if (!matchesProviderHints(receiverText, preset, importedModules)) {
        return undefined;
    }
    const tableName = findPrismaModelName(receiver);
    if (!tableName) {
        return undefined;
    }
    return buildUsageCandidate(call, projectName, relPath, preset, methodName, operationType, [tableName], maxTablesPerOperation);
}
function tryRecognizeKnex(call, preset, projectName, relPath, methodName, maxTablesPerOperation, importedModules) {
    const operationType = getOperationType(methodName, preset);
    if (!operationType) {
        return undefined;
    }
    const receiver = getReceiver(call);
    const receiverText = receiver?.getText() ?? '';
    if (!matchesProviderHints(receiverText, preset, importedModules)) {
        return undefined;
    }
    const tableName = extractKnexTableName(receiver);
    if (!tableName) {
        return undefined;
    }
    return buildUsageCandidate(call, projectName, relPath, preset, methodName, operationType, [tableName], maxTablesPerOperation);
}
function tryRecognizeCypher(call, preset, projectName, relPath, methodName, maxTablesPerOperation, importedModules) {
    if (!supportsMethod(methodName, preset)) {
        return undefined;
    }
    const receiver = getReceiver(call);
    const receiverText = receiver?.getText() ?? '';
    if (!matchesProviderHints(receiverText, preset, importedModules)) {
        return undefined;
    }
    const statementText = resolveStatementFromArguments(call, preset);
    if (!looksLikeCypher(statementText)) {
        return undefined;
    }
    const operationType = inferCypherOperationType(statementText);
    if (!operationType) {
        return undefined;
    }
    const tables = extractCypherEntities(statementText, maxTablesPerOperation);
    if (tables.length === 0) {
        return undefined;
    }
    return buildUsageCandidate(call, projectName, relPath, preset, methodName, operationType, tables, maxTablesPerOperation);
}
function buildUsageCandidate(call, projectName, relPath, preset, methodName, operationType, tables, maxTablesPerOperation) {
    return {
        sourceId: resolveSourceId(call, projectName, relPath),
        lineNumber: call.getStartLineNumber(),
        recognizerId: preset.id,
        provider: preset.provider,
        methodName,
        operationType,
        tables: tables
            .map(normalizeTableName)
            .filter(name => name.length > 0)
            .filter((name, index, values) => values.findIndex(other => other.toLowerCase() === name.toLowerCase()) === index)
            .slice(0, Math.max(1, maxTablesPerOperation)),
    };
}
function extractKnexTableName(receiver) {
    if (!receiver) {
        return undefined;
    }
    if (receiver.isKind(SyntaxKind.CallExpression)) {
        const callee = receiver.getExpression();
        const firstArgument = receiver.getArguments()[0];
        if (callee.isKind(SyntaxKind.Identifier)) {
            return normalizeTableName(resolveStatementText(firstArgument));
        }
        if (callee.isKind(SyntaxKind.PropertyAccessExpression)) {
            const methodName = callee.getName();
            if (methodName === 'from' || methodName === 'into') {
                return normalizeTableName(resolveStatementText(firstArgument));
            }
            return extractKnexTableName(callee.getExpression());
        }
    }
    if (receiver.isKind(SyntaxKind.PropertyAccessExpression)) {
        return extractKnexTableName(receiver.getExpression());
    }
    return undefined;
}
function findPrismaModelName(receiver) {
    if (!receiver?.isKind(SyntaxKind.PropertyAccessExpression)) {
        return undefined;
    }
    return normalizeTableName(receiver.getName());
}
function resolveStatementFromArguments(call, preset) {
    const args = call.getArguments();
    if (args.length === 0) {
        return undefined;
    }
    for (const index of preset.statementArgumentIndexes.length > 0 ? preset.statementArgumentIndexes : [0]) {
        const arg = args[index];
        const resolved = resolveStatementText(arg);
        if (resolved) {
            return resolved;
        }
    }
    for (const arg of args) {
        const resolved = resolveStatementText(arg);
        if (resolved) {
            return resolved;
        }
    }
    return undefined;
}
function resolveStatementText(node, depth = 0) {
    if (!node || depth > 6) {
        return undefined;
    }
    const direct = resolveStringLiteralValue(node);
    if (direct) {
        return direct;
    }
    if (node.isKind(SyntaxKind.TemplateExpression)) {
        const head = node.getHead().getLiteralText();
        const spans = node.getTemplateSpans().map(span => span.getLiteral().getLiteralText());
        return `${head}${spans.join('')}`;
    }
    if (node.isKind(SyntaxKind.BinaryExpression) && node.getOperatorToken().getKind() === SyntaxKind.PlusToken) {
        const left = resolveStatementText(node.getLeft(), depth + 1);
        const right = resolveStatementText(node.getRight(), depth + 1);
        return left || right ? `${left ?? ''}${right ?? ''}` : undefined;
    }
    if (node.isKind(SyntaxKind.Identifier)) {
        for (const definition of node.getDefinitionNodes()) {
            if (definition.isKind(SyntaxKind.VariableDeclaration)) {
                const initializer = definition.getInitializer();
                const resolved = resolveStatementText(initializer, depth + 1);
                if (resolved) {
                    return resolved;
                }
            }
        }
    }
    return undefined;
}
function resolveSourceId(node, projectName, relPath) {
    const classDecl = node.getFirstAncestorByKind(SyntaxKind.ClassDeclaration);
    const methodDecl = node.getFirstAncestorByKind(SyntaxKind.MethodDeclaration);
    if (classDecl && methodDecl) {
        return nodeId(projectName, relPath, `${classDecl.getName() ?? '<anonymous>'}.${methodDecl.getName()}`, 'Method');
    }
    const ctorDecl = node.getFirstAncestorByKind(SyntaxKind.Constructor);
    if (classDecl && ctorDecl) {
        return nodeId(projectName, relPath, `${classDecl.getName() ?? '<anonymous>'}.constructor`, 'Method');
    }
    const functionDecl = node.getFirstAncestorByKind(SyntaxKind.FunctionDeclaration);
    if (functionDecl?.getName()) {
        return nodeId(projectName, relPath, functionDecl.getName(), 'Method');
    }
    const variableDecl = node.getFirstAncestorByKind(SyntaxKind.VariableDeclaration);
    if (isFunctionValuedVariable(variableDecl)) {
        return nodeId(projectName, relPath, variableDecl.getName(), 'Method');
    }
    if (classDecl?.getName()) {
        return nodeId(projectName, relPath, classDecl.getName(), 'Class');
    }
    return fileId(projectName, relPath);
}
function buildOperationNodeId(projectName, sourceId, relPath, lineNumber, usage) {
    const payload = `${projectName}|${sourceId}|${relPath}|${lineNumber}|${usage.provider}|${usage.methodName}|${usage.operationType}|${usage.tables.join(',')}`;
    const hash = createHash('sha256').update(payload, 'utf8').digest('hex').slice(0, 16).toUpperCase();
    return `${projectName}::ExternalConcept::DatabaseOperation::${hash}`;
}
function supportsLanguage(preset, language) {
    return preset.languages.length === 0 ||
        preset.languages.some(candidate => candidate.localeCompare(language, undefined, { sensitivity: 'accent' }) === 0);
}
function supportsMethod(methodName, preset) {
    return [...preset.readMethods, ...preset.writeMethods]
        .some(candidate => candidate.toLowerCase() === methodName.toLowerCase());
}
function matchesProviderHints(receiverText, preset, importedModules) {
    const normalizedReceiver = receiverText.toLowerCase();
    const receiverMatch = preset.receiverTextHints.length === 0 ||
        preset.receiverTextHints.some(hint => normalizedReceiver.includes(hint.toLowerCase()));
    const importMatch = preset.importModuleHints.length === 0 ||
        [...importedModules].some(moduleName => preset.importModuleHints.some(hint => moduleName.toLowerCase() === hint.toLowerCase()));
    return receiverMatch || importMatch;
}
function getOperationType(methodName, preset) {
    if (preset.readMethods.some(candidate => candidate.toLowerCase() === methodName.toLowerCase())) {
        return 'Reads';
    }
    if (preset.writeMethods.some(candidate => candidate.toLowerCase() === methodName.toLowerCase())) {
        return 'Writes';
    }
    return undefined;
}
function getMemberName(call) {
    const expression = call.getExpression();
    if (expression.isKind(SyntaxKind.PropertyAccessExpression)) {
        return expression.getName();
    }
    if (expression.isKind(SyntaxKind.Identifier)) {
        return expression.getText();
    }
    return undefined;
}
function getReceiver(call) {
    const expression = call.getExpression();
    return expression.isKind(SyntaxKind.PropertyAccessExpression) ? expression.getExpression() : undefined;
}
function inferCypherOperationType(statementText) {
    if (cypherWriteRegex.test(statementText)) {
        return 'Writes';
    }
    if (cypherReadRegex.test(statementText)) {
        return 'Reads';
    }
    return undefined;
}
function looksLikeCypher(statementText) {
    return typeof statementText === 'string' && (cypherReadRegex.test(statementText) || cypherWriteRegex.test(statementText));
}
function extractCypherEntities(statementText, maxTablesPerOperation) {
    const matches = [...statementText.matchAll(cypherLabelRegex), ...statementText.matchAll(cypherRelationshipRegex)];
    return matches
        .map(match => normalizeTableName(match.groups?.name))
        .filter((value, index, values) => value.length > 0 && values.findIndex(other => other.toLowerCase() === value.toLowerCase()) === index)
        .slice(0, Math.max(1, maxTablesPerOperation));
}
function normalizeTableName(value) {
    if (!value) {
        return '';
    }
    return value.trim()
        .replace(/^[\[\]"'`]+|[\[\]"'`]+$/g, '')
        .replace(/\.+$/g, '');
}
function dedupeUsages(usages) {
    const seen = new Set();
    const deduped = [];
    for (const usage of usages) {
        const key = [
            usage.sourceId,
            usage.lineNumber,
            usage.provider,
            usage.methodName,
            usage.operationType,
            usage.tables.join('|'),
        ].join('::');
        if (seen.has(key)) {
            continue;
        }
        seen.add(key);
        deduped.push(usage);
    }
    return deduped;
}
function isFunctionValuedVariable(variable) {
    if (!variable) {
        return false;
    }
    const initializer = variable.getInitializer();
    return initializer?.isKind(SyntaxKind.ArrowFunction) === true
        || initializer?.isKind(SyntaxKind.FunctionExpression) === true;
}
export const __testing = {
    extractCypherEntities,
    inferCypherOperationType,
    looksLikeCypher,
    normalizeTableName,
    resolveStatementText,
    sqlStatementRegex,
    sqlTableRegex,
};
