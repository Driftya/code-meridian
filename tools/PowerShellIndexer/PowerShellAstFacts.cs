using System.Management.Automation.Language;

namespace CodeMeridian.PowerShellIndexer;

internal static class PowerShellAstFacts
{
    internal static PowerShellSourceSpan Span(IScriptExtent extent) => new(extent.StartOffset, extent.EndOffset - extent.StartOffset,
        extent.StartLineNumber, extent.StartColumnNumber, extent.EndLineNumber, extent.EndColumnNumber);

    internal static string Clip(string text, int limit = 2000) => text.Length > limit ? text[..limit] : text;

    internal static (string Kind, string? Value) PathExpression(Ast ast) => ast switch
    {
        StringConstantExpressionAst literal => ("literal", literal.Value),
        ExpandableStringExpressionAst value when value.NestedExpressions.Count == 1
            && value.NestedExpressions[0] is VariableExpressionAst variable
            && variable.VariablePath.UserPath.Equals("PSScriptRoot", StringComparison.OrdinalIgnoreCase)
            && value.Value.StartsWith("$PSScriptRoot/", StringComparison.OrdinalIgnoreCase)
            && !value.Value[14..].Contains('$') && !value.Extent.Text.Contains('`') => ("script_root", value.Value[14..]),
        _ => ("dynamic", null)
    };

    internal static FunctionDefinitionAst? Owner(Ast ast)
    {
        for (var parent = ast.Parent; parent is not null; parent = parent.Parent)
            if (parent is FunctionDefinitionAst function) return function;
        return null;
    }

    internal static bool Conditional(Ast ast, FunctionDefinitionAst? owner)
    {
        for (var parent = ast.Parent; parent is not null && parent != owner?.Body; parent = parent.Parent)
            if (parent is not (NamedBlockAst or StatementBlockAst or ScriptBlockAst)) return true;
        return false;
    }

    internal static bool Deferred(Ast ast)
    {
        for (var parent = ast.Parent; parent is not null; parent = parent.Parent)
            if (parent is ScriptBlockExpressionAst or TypeDefinitionAst) return true;
        return false;
    }
}
