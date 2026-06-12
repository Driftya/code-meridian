using CodeMeridian.RoslynIndexer.Pipeline;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class RouteConstantResolverTests
{
    [Fact]
    public void BuildStringConstants_ResolvesFieldAndLocalStringLiterals()
    {
        var tree = CSharpSyntaxTree.ParseText(
            """
            namespace Demo;

            public static class Routes
            {
                private const string Base = "/api";

                public static void Map()
                {
                    const string Orders = "/api/orders";
                }
            }
            """);

        var constants = RouteConstantResolver.BuildStringConstants(tree.GetCompilationUnitRoot());

        constants.Should().ContainKey("Base").WhoseValue.Should().Be("/api");
        constants.Should().ContainKey("Orders").WhoseValue.Should().Be("/api/orders");
    }

    [Fact]
    public void ResolveStringExpression_ResolvesInterpolatedPlaceholders()
    {
        var expression = (InterpolatedStringExpressionSyntax)CSharpSyntaxTree.ParseText("""
            $"api/{id}/orders"
            """).GetCompilationUnitRoot().DescendantNodes().OfType<InterpolatedStringExpressionSyntax>().Single();

        var result = RouteConstantResolver.ResolveStringExpression(expression, new Dictionary<string, string>());

        result.Should().Be("api/{param}/orders");
    }
}
