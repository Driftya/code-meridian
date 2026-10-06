using CodeMeridian.RoslynIndexer.Pipeline;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class PackageSymbolIdentityTests
{
    [Fact]
    public void ForwardedType_KeepsDefiningAssemblyIdentityAndPackageFacadeHint()
    {
        var runtime = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        static byte[] Emit(CSharpCompilation compilation)
        {
            using var output = new MemoryStream();
            var emitted = compilation.Emit(output);
            emitted.Success.Should().BeTrue(string.Join(";", emitted.Diagnostics));
            return output.ToArray();
        }
        var definition = MetadataReference.CreateFromImage(Emit(CSharpCompilation.Create("Definition",
            [CSharpSyntaxTree.ParseText("namespace Shared; public class Value { }")], [runtime],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))));
        var facade = MetadataReference.CreateFromImage(Emit(CSharpCompilation.Create("Facade",
            [CSharpSyntaxTree.ParseText("[assembly:System.Runtime.CompilerServices.TypeForwardedTo(typeof(Shared.Value))]")],
            [runtime, definition], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))));
        var consumer = CSharpCompilation.Create("Consumer", references: [runtime, definition, facade]);
        var hints = PackageTypeForwarding.Build(consumer, _ => new PackageAsset("Facade.Package", "1.0.0", null, null, "lib/net10.0/Facade.dll", "net10.0"));
        var type = consumer.GetTypeByMetadataName("Shared.Value")!;
        type.ContainingAssembly.Name.Should().Be("Definition");
        hints.Single().Key.Should().StartWith(type.ContainingAssembly.Identity.ToString());
        hints.Single().Value.Single().AssemblyIdentity.Should().StartWith("Facade,");
    }

    [Fact]
    public void Key_RoundTripsNestedGenericArrayAndRefSignaturesAcrossMetadata()
    {
        var source = CSharpSyntaxTree.ParseText("""
            namespace Shared;
            public interface ICheck { void Check(int value); }
            public class Outer<T>
            {
                public class Inner : ICheck
                {
                    public T[] Map<U>(ref T[] values, U argument) => values;
                    public void Fill(out int value) { value = 1; }
                    void ICheck.Check(int value) { }
                }
            }
            """);
        var runtime = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var compilation = CSharpCompilation.Create("Shared", [source], [runtime],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        emitted.Success.Should().BeTrue(string.Join(";", emitted.Diagnostics));
        var metadata = CSharpCompilation.Create("Consumer", references: [runtime, MetadataReference.CreateFromImage(output.ToArray())]);
        var definition = compilation.GetTypeByMetadataName("Shared.Outer`1+Inner")!;
        var imported = metadata.GetTypeByMetadataName("Shared.Outer`1+Inner")!;
        foreach (var method in definition.GetMembers().OfType<IMethodSymbol>())
        {
            var key = PackageSymbolIdentity.Key(method);
            imported.GetMembers().OfType<IMethodSymbol>().Select(PackageSymbolIdentity.Key).Should().Contain(key);
        }
        PackageSymbolIdentity.Key(definition.GetMembers("Map").Single()).Should().EndWith("|Ref,None");
        PackageSymbolIdentity.Key(definition.GetMembers("Fill").Single()).Should().EndWith("|Out");
    }
}
