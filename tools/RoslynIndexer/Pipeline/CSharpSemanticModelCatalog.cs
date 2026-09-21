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
        var sourceFiles = files.ToArray();
        var projectInputs = CSharpSemanticProjectInputs.Read(sourceFiles);
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Preview,
            DocumentationMode.Parse);
        var trees = sourceFiles
            .Select(file => CSharpSyntaxTree.ParseText(
                File.ReadAllText(file.FullName),
                parseOptions,
                file.FullName,
                Encoding.UTF8))
            .ToArray();
        IEnumerable<SyntaxTree> compilationTrees = trees;
        if (projectInputs.UseCommonImplicitUsings)
        {
            compilationTrees = compilationTrees.Append(CSharpSyntaxTree.ParseText("""
                global using System;
                global using System.Collections.Generic;
                global using System.IO;
                global using System.Linq;
                global using System.Net.Http;
                global using System.Threading;
                global using System.Threading.Tasks;
                """, parseOptions));
        }
        var references = BuildRuntimeReferences().OfType<PortableExecutableReference>().ToList();
        var assemblyNames = references.Select(reference => Path.GetFileName(reference.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in projectInputs.AssemblyPaths)
        {
            if (!assemblyNames.Add(Path.GetFileName(assembly))) continue;
            try { references.Add(MetadataReference.CreateFromFile(assembly)); }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException) { }
        }
        var compilation = CSharpCompilation.Create(
            "CodeMeridian.RelationshipSemanticModel",
            compilationTrees,
            references,
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
