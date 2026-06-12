using CodeMeridian.RoslynIndexer.Pipeline;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class RouteExtractionTests
{
    [Fact]
    public void AspNetControllerRouteExtractor_ExtractsControllerTokens()
    {
        var tree = CSharpSyntaxTree.ParseText(
            """
            using Microsoft.AspNetCore.Mvc;

            [Route("api/[controller]")]
            public class OrdersController : ControllerBase
            {
                [HttpGet("[action]/{id:int}")]
                public IActionResult Get(int id) => Ok();
            }
            """);

        var root = tree.GetCompilationUnitRoot();
        var nodes = new List<IngestNodeRequest>
        {
            new("Project::Method::OrdersController.Get(int)", "Get(int)", "Method", null, "src/Orders.cs", 6, null)
        };
        var edges = new List<IngestEdgeRequest>();
        var constants = RouteConstantResolver.BuildStringConstants(root);

        AspNetControllerRouteExtractor.Extract(root, "src/Orders.cs", "Project", nodes, edges, constants);

        nodes.Should().Contain(node => node.Id == "Project::ApiEndpoint::GET /api/orders/get/{param}");
        edges.Should().Contain(edge =>
            edge.SourceId == "Project::Method::OrdersController.Get(int)"
            && edge.TargetId == "Project::ApiEndpoint::GET /api/orders/get/{param}");
    }

    [Fact]
    public void RouteSourceResolver_FallsBackToContainingMethodForLambdaHandlers()
    {
        var tree = CSharpSyntaxTree.ParseText(
            """
            using Microsoft.AspNetCore.Builder;

            public static class Routes
            {
                public static void Map(WebApplication app)
                {
                    app.MapGet("/api/orders", () => Results.Ok());
                }
            }
            """);

        var invocation = tree.GetCompilationUnitRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .Single(node => node.Expression.ToString().Contains("MapGet"));
        var nodes = new List<IngestNodeRequest>
        {
            new("Project::Method::Routes.Map(WebApplication)", "Map(WebApplication)", "Method", null, "src/Routes.cs", 5, null)
        };

        var sourceId = RouteSourceResolver.ResolveMinimalApiSourceId(invocation, "src/Routes.cs", nodes, "Project");

        sourceId.Should().Be("Project::Method::Routes.Map(WebApplication)");
    }
}
