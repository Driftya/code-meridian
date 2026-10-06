using CodeMeridian.Core.CodeGraph;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal sealed class PackageReferenceExtractor(
    string rootPath, PackageBuildScope scope, Compilation compilation,
    PackageAssetCatalog assets, IReadOnlyList<IngestNodeRequest> sourceNodes, PackageIndexingOptions options,
    IReadOnlyDictionary<string, PackageAsset>? sourceProjects = null)
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<PackageForwardingAssembly>> _forwarding = PackageTypeForwarding.Build(compilation, assets.Find);
    public List<PackageSymbolExport> Exports { get; } = [];
    public List<PackageSymbolReference> References { get; } = [];

    public void Extract(SyntaxTree tree, CancellationToken cancellationToken)
    {
        var file = Path.GetRelativePath(rootPath, tree.FilePath).Replace('\\', '/');
        if (file.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(file)) return;
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot(cancellationToken);
        var declarations = new Dictionary<SyntaxNode, IngestNodeRequest>();
        foreach (var node in root.DescendantNodes().Where(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax
                     or MethodDeclarationSyntax or ConstructorDeclarationSyntax or PropertyDeclarationSyntax or LocalFunctionStatementSyntax))
        {
            var symbol = model.GetDeclaredSymbol(node, cancellationToken);
            if (symbol is null || PackageSymbolIdentity.Key(symbol) is not { } key) continue;
            var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var kind = symbol is IMethodSymbol ? "Method" : symbol is IPropertySymbol ? "Property" : null;
            var matches = sourceNodes.Where(source => source.FilePath == file && source.LineNumber == line
                && (kind is null ? source.Type is "Class" or "Struct" or "Interface" or "Enum" or "Delegate" : source.Type == kind)
                && (source.Properties?.GetValueOrDefault("declarationStart") is not { } start || start == node.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                .ToArray();
            if (matches.Length != 1) continue;
            declarations[node] = matches[0];
            Exports.Add(new(PackageSymbolIdentity.Id(scope.Id, key, matches[0].Id, file), scope.Id, matches[0].Id, key, file));
            if (symbol is INamedTypeSymbol namedType)
            {
                foreach (var constructor in namedType.InstanceConstructors.Where(constructor => constructor.IsImplicitlyDeclared))
                {
                    if (PackageSymbolIdentity.Key(constructor) is { } constructorKey)
                        Exports.Add(new(PackageSymbolIdentity.Id(scope.Id, constructorKey, matches[0].Id, file),
                            scope.Id, matches[0].Id, constructorKey, file));
                }
            }
        }
        foreach (var node in root.DescendantNodes())
        {
            ISymbol? target = node switch
            {
                InvocationExpressionSyntax or ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax
                    => model.GetSymbolInfo(node, cancellationToken).Symbol,
                TypeSyntax type when IsTypePosition(type) => model.GetTypeInfo(type, cancellationToken).Type,
                _ => null
            };
            while (target is IArrayTypeSymbol array) target = array.ElementType;
            while (target is IPointerTypeSymbol pointer) target = pointer.PointedAtType;
            if (target is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
                target = nullable.TypeArguments[0];
            if (target is null || target is IErrorTypeSymbol || target.ContainingAssembly is null) continue;
            target = PackageSymbolIdentity.Definition(target);
            if (SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, compilation.Assembly)) continue;
            var owner = node.Ancestors().FirstOrDefault(declarations.ContainsKey);
            if (owner is null || PackageSymbolIdentity.Key(target) is not { } key) continue;
            var reference = compilation.GetMetadataReference(target.ContainingAssembly) as PortableExecutableReference;
            var asset = assets.Find(reference?.FilePath);
            if (asset is null && target.Locations.Any(location => location.IsInSource))
            {
                var declarationPath = target.Locations.First(location => location.IsInSource).SourceTree?.FilePath;
                if (declarationPath is not null && !Path.GetRelativePath(rootPath, declarationPath).Replace('\\', '/').StartsWith("../", StringComparison.Ordinal)) continue;
                asset = sourceProjects?.GetValueOrDefault(target.ContainingAssembly.Identity.ToString());
            }
            // Framework metadata does not create a per-symbol external graph. Source-project references are kept.
            if (asset is null && !target.Locations.Any(location => location.IsInSource)) continue;
            var span = node.GetLocation().GetLineSpan();
            var relation = target is IMethodSymbol ? "Calls" : node.Parent is BaseTypeSyntax
                ? target is INamedTypeSymbol { TypeKind: TypeKind.Interface } ? "Implements" : "Inherits" : "Uses";
            var binding = asset is null ? null : options.ProducerBindings.GetValueOrDefault(asset.Id);
            References.Add(new(PackageSymbolIdentity.Id(scope.Id, declarations[owner].Id, key, node.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                scope.Id, declarations[owner].Id, key, target.ContainingAssembly.Name, target.ContainingAssembly.Identity.ToString(),
                asset?.Id, asset?.Version, asset?.RepositoryUrl, asset?.Commit, relation, file,
                span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
                span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1, binding, asset?.CompileAsset, asset?.Framework,
                reference is null ? "source_project" : "nuget")
            {
                ForwardingAssemblies = _forwarding.GetValueOrDefault(target.ContainingAssembly.Identity + "::"
                    + PackageSymbolIdentity.Key(target is INamedTypeSymbol ? target : target.ContainingType!)) ?? []
            });
        }
    }

    private static bool IsTypePosition(TypeSyntax type) => type.Parent switch
    {
        VariableDeclarationSyntax declaration => declaration.Type == type,
        ParameterSyntax parameter => parameter.Type == type,
        MethodDeclarationSyntax method => method.ReturnType == type,
        PropertyDeclarationSyntax property => property.Type == type,
        ObjectCreationExpressionSyntax creation => creation.Type == type,
        CastExpressionSyntax cast => cast.Type == type,
        TypeOfExpressionSyntax expression => expression.Type == type,
        BaseTypeSyntax or TypeArgumentListSyntax or DeclarationPatternSyntax => true,
        _ => false
    };
}
