using CodeMeridian.RoslynIndexer.Pipeline;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class CSharpIndexerSyntaxUtilitiesTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"codemeridian-roslyn-{Guid.NewGuid():N}"));

    [Fact]
    public void ResolveStringExpression_ResolvesLocalStringDeclarationUsedByIdentifier()
    {
        var root = CSharpSyntaxTree.ParseText(
            """
            namespace Demo;

            public sealed class EndpointBuilder
            {
                public void Map()
                {
                    const string Prefix = "/api";
                    var route = $"{Prefix}/orders";
                    MapGet(route);
                }
            }
            """).GetCompilationUnitRoot();
        var identifier = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.Expression.ToString() == "MapGet")
            .ArgumentList
            .Arguments
            .Single()
            .Expression;

        var result = CSharpIndexerSyntaxUtilities.ResolveStringExpression(
            identifier,
            CSharpConfigurationConstantRegistry.Build([]));

        result.Should().Be("/api/orders");
    }

    [Fact]
    public void ResolveStringExpression_ResolvesMemberAccessInsideInterpolatedString()
    {
        var constantsFile = new FileInfo(Path.Combine(_root.FullName, "RouteConstants.cs"));
        File.WriteAllText(
            constantsFile.FullName,
            """
            namespace Demo;

            public static class RouteConstants
            {
                public const string Base = "/api";
            }
            """);
        var expression = CSharpSyntaxTree.ParseText(
            """
            namespace Demo;

            public sealed class EndpointBuilder
            {
                public void Map()
                {
                    MapGet($"{RouteConstants.Base}/orders");
                }
            }
            """).GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<InterpolatedStringExpressionSyntax>()
            .Single();

        var result = CSharpIndexerSyntaxUtilities.ResolveStringExpression(
            expression,
            CSharpConfigurationConstantRegistry.Build([constantsFile]));

        result.Should().Be("/api/orders");
    }

    public void Dispose()
    {
        _root.Delete(recursive: true);
    }
}
