using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal sealed class CSharpSemanticModelCatalog
{
    private readonly IReadOnlyDictionary<string, CSharpSemanticFile> _files;

    private CSharpSemanticModelCatalog(IReadOnlyDictionary<string, CSharpSemanticFile> files) =>
        _files = files;

    public static CSharpSemanticModelCatalog Create(IEnumerable<FileInfo> files)
    {
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Preview,
            DocumentationMode.Parse);
        var trees = files
            .Select(file => CSharpSyntaxTree.ParseText(
                File.ReadAllText(file.FullName),
                parseOptions,
                file.FullName,
                Encoding.UTF8))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "CodeMeridian.RelationshipSemanticModel",
            trees,
            BuildRuntimeReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable));
        var semanticFiles = trees.ToDictionary(
            tree => Path.GetFullPath(tree.FilePath),
            tree => new CSharpSemanticFile(
                tree.GetCompilationUnitRoot(),
                compilation.GetSemanticModel(tree, ignoreAccessibility: true)),
            StringComparer.OrdinalIgnoreCase);

        return new CSharpSemanticModelCatalog(semanticFiles);
    }

    public CSharpSemanticFile? Find(FileInfo file) =>
        _files.GetValueOrDefault(Path.GetFullPath(file.FullName));

    private static IEnumerable<MetadataReference> BuildRuntimeReferences()
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedAssemblies))
            return [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)];

        return trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
    }
}

internal sealed record CSharpSemanticFile(
    CompilationUnitSyntax Root,
    SemanticModel SemanticModel);
