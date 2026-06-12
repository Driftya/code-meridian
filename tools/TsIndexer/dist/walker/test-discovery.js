import { SyntaxKind } from 'ts-morph';
import { buildSyntheticTestCaseName, isTestFilePath, resolveStringLiteralValue, syntheticTestMethodId } from './common.js';
export function extractIndexedTestCases(sourceFile, projectName, relPath) {
    if (!isTestFilePath(relPath))
        return [];
    const testCases = [];
    for (const call of sourceFile.getDescendantsOfKind(SyntaxKind.CallExpression)) {
        const testInvoker = getTestInvokerName(call.getExpression());
        if (!testInvoker)
            continue;
        const callback = [...call.getArguments()]
            .reverse()
            .find(arg => arg.getKind() === SyntaxKind.ArrowFunction || arg.getKind() === SyntaxKind.FunctionExpression);
        if (!callback)
            continue;
        const label = resolveStringLiteralValue(call.getArguments()[0]) ?? `line-${call.getStartLineNumber()}`;
        const lineNumber = callback.getStartLineNumber();
        const endLineNumber = callback.getEndLineNumber();
        const stableName = buildSyntheticTestCaseName(testInvoker, label, lineNumber);
        testCases.push({
            callback,
            id: syntheticTestMethodId(projectName, relPath, stableName),
            lineNumber,
            lineCount: endLineNumber - lineNumber + 1,
            name: stableName,
        });
    }
    return testCases;
}
function getTestInvokerName(node) {
    if (node.getKind() === SyntaxKind.Identifier) {
        const name = node.getText();
        return name === 'it' || name === 'test' ? name : undefined;
    }
    if (node.getKind() === SyntaxKind.PropertyAccessExpression) {
        const propertyAccess = node.asKindOrThrow(SyntaxKind.PropertyAccessExpression);
        return getTestInvokerName(propertyAccess.getExpression());
    }
    if (node.getKind() === SyntaxKind.CallExpression) {
        return getTestInvokerName(node.asKindOrThrow(SyntaxKind.CallExpression).getExpression());
    }
    return undefined;
}
