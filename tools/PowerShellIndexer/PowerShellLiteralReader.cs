using System.Management.Automation.Language;

namespace CodeMeridian.PowerShellIndexer;

// Deliberately does not call SafeGetValue, GetPowerShell, a runspace, or an evaluator.
internal static class PowerShellLiteralReader
{
    internal static bool TryRead(Ast ast, out object? value, int depth = 0)
    {
        value = null;
        if (depth > 16) return false;
        switch (ast)
        {
            case StringConstantExpressionAst text when text.Value.Length <= 2048:
                value = text.Value;
                return true;
            case ConstantExpressionAst constant when constant.Value is null or bool or int or long or double or decimal:
                value = constant.Value;
                return true;
            case VariableExpressionAst variable when !variable.Splatted:
                switch (variable.VariablePath.UserPath.ToLowerInvariant())
                {
                    case "true": value = true; return true;
                    case "false": value = false; return true;
                    case "null": return true;
                    default: return false;
                }
            case StatementBlockAst block when block.Statements.Count == 1 && block.Traps is null or { Count: 0 }:
                return TryRead(block.Statements[0], out value, depth + 1);
            case PipelineAst pipeline when pipeline.PipelineElements.Count == 1:
                return TryRead(pipeline.PipelineElements[0], out value, depth + 1);
            case CommandExpressionAst expression when expression.Redirections.Count == 0:
                return TryRead(expression.Expression, out value, depth + 1);
            case ParenExpressionAst paren:
                return TryRead(paren.Pipeline, out value, depth + 1);
            case ArrayExpressionAst array:
                return ReadArray(array.SubExpression.Statements, out value, depth);
            case ArrayLiteralAst array:
                return ReadArray(array.Elements, out value, depth);
            case HashtableAst table when table.KeyValuePairs.Count <= 128:
                var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in table.KeyValuePairs)
                {
                    if (!TryRead(pair.Item1, out var key, depth + 1) || key is not string name || name.Length > 256
                        || !TryRead(pair.Item2, out var item, depth + 1) || !fields.TryAdd(name, item)) return false;
                }
                value = fields;
                return true;
            default: return false;
        }
    }

    private static bool ReadArray(IEnumerable<Ast> nodes, out object? value, int depth)
    {
        var items = new List<object?>();
        foreach (var node in nodes)
        {
            if (items.Count == 512 || !TryRead(node, out var item, depth + 1)) { value = null; return false; }
            // @( 'a', 'b' ) has one ArrayLiteralAst inside its pipeline.
            if (item is object?[] nested) items.AddRange(nested);
            else items.Add(item);
            if (items.Count > 512) { value = null; return false; }
        }
        value = items.ToArray();
        return true;
    }

    internal static string[]? Names(object? value)
    {
        var items = value is string name ? [name] : value is object?[] array && array.All(item => item is string)
            ? array.Cast<string>().ToArray() : null;
        return items is null || items.Any(item => item.Length == 0 || item.IndexOfAny(['*', '?', '[', ']']) >= 0)
            ? null : items;
    }
}
