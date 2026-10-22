using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMeridian.RoslynIndexer.Pipeline;

/// <summary>Compares source declarations with emitted identities, independently of edge extraction.</summary>
internal static class CSharpCallableDefinitionAuditor
{
    public static (CSharpCallableDefinitionSummary Summary, IReadOnlyList<CSharpAnalysisDiagnostic> Samples) Analyze(
        IReadOnlyList<(string Path, CSharpSemanticFile File)> files,
        IReadOnlyList<IngestNodeRequest> nodes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var byLocation = nodes.Where(node => node.Type is "Method" or "Operator" or "Indexer")
            .Where(node => node.Properties?.ContainsKey("declarationStart") == true)
            .ToLookup(node => (node.FilePath, node.Type, node.Properties!["declarationStart"]));
        var represented = new List<(string Id, ISymbol? Symbol, string Path, SyntaxNode Syntax)>();
        var samples = new List<CSharpAnalysisDiagnostic>();
        var candidates = 0;
        var missing = 0;
        foreach (var (path, file) in files.OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            foreach (var syntax in file.Root.DescendantNodesAndSelf())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCandidate(syntax)) continue;
                candidates++;
                var expectedType = syntax switch
                {
                    MethodDeclarationSyntax or ConstructorDeclarationSyntax or LocalFunctionStatementSyntax => "Method",
                    OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax => "Operator",
                    IndexerDeclarationSyntax => "Indexer",
                    _ => null
                };
                var matches = expectedType is null ? [] : byLocation[(path, expectedType,
                    syntax.SpanStart.ToString(CultureInfo.InvariantCulture))].ToArray();
                if (matches.Length != 1)
                {
                    missing++;
                    Sample(path, syntax, "unrepresented_callable");
                    continue;
                }

                var symbol = file.SemanticModel.GetDeclaredSymbol(syntax, cancellationToken);
                // A partial method's declaration and implementation deliberately share an identity.
                if (symbol is IMethodSymbol method) symbol = method.PartialDefinitionPart ?? method;
                represented.Add((matches[0].Id, symbol, path, syntax));
            }
        }

        var conflicts = 0;
        foreach (var group in represented.GroupBy(item => item.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Count() < 2) continue;
            var symbols = group.Select(item => item.Symbol).ToArray();
            if (symbols.All(symbol => symbol is not null)
                && symbols.Distinct(SymbolEqualityComparer.Default).Count() == 1) continue;
            conflicts += group.Count();
            foreach (var item in group) Sample(item.Path, item.Syntax, "callable_identity_conflict");
        }

        return (new(candidates, represented.Count, missing, conflicts), samples);

        void Sample(string path, SyntaxNode syntax, string code)
        {
            if (samples.Count < 8)
                samples.Add(new(path, "definitions", code,
                    syntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
        }
    }

    private static bool IsCandidate(SyntaxNode syntax) => syntax switch
    {
        BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or IndexerDeclarationSyntax
            or AccessorDeclarationSyntax or AnonymousFunctionExpressionSyntax => true,
        PropertyDeclarationSyntax property => property.ExpressionBody is not null,
        ClassDeclarationSyntax type => type.ParameterList is not null,
        StructDeclarationSyntax type => type.ParameterList is not null,
        RecordDeclarationSyntax type => type.ParameterList is not null,
        CompilationUnitSyntax unit => unit.Members.OfType<GlobalStatementSyntax>().Any(),
        _ => false
    };
}
