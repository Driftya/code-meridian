using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal sealed class CSharpConfigurationConstantRegistry
{
    private readonly Dictionary<string, string> _values;

    private CSharpConfigurationConstantRegistry(Dictionary<string, string> values) => _values = values;

    public static CSharpConfigurationConstantRegistry Build(IEnumerable<FileInfo> files)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file.FullName), path: file.FullName).GetCompilationUnitRoot();
            foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            {
                if (!field.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.ConstKeyword)) || field.Declaration.Type.ToString() != "string")
                    continue;

                foreach (var variable in field.Declaration.Variables)
                {
                    if (variable.Initializer?.Value is not LiteralExpressionSyntax literal ||
                        !literal.IsKind(SyntaxKind.StringLiteralExpression))
                        continue;

                    var namespaceName = field.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();
                    var typeName = field.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
                    var value = literal.Token.ValueText;

                    values[variable.Identifier.ValueText] = value;
                    if (!string.IsNullOrWhiteSpace(typeName))
                    {
                        values[$"{typeName}.{variable.Identifier.ValueText}"] = value;
                        if (!string.IsNullOrWhiteSpace(namespaceName))
                            values[$"{namespaceName}.{typeName}.{variable.Identifier.ValueText}"] = value;
                    }
                }
            }
        }

        return new CSharpConfigurationConstantRegistry(values);
    }

    public bool TryResolve(ExpressionSyntax expression, out string? value) =>
        _values.TryGetValue(expression.ToString(), out value);
}
