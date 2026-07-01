using CodeMeridian.RoslynIndexer.Pipeline;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeMeridian.Indexer.Tests.Roslyn;

public sealed class CSharpAstWalkerTests
{
    [Fact]
    public void InterfaceStaticAbstractMembers_AreIndexedAndContained()
    {
        const string source = """
            namespace Demo;

            public interface IFactory
            {
                static abstract Point Create();
            }
            """;

        var (nodes, edges) = ExtractGraph(source, "src/Factory.cs");

        nodes.Should().Contain(node => node.Type == "Interface" && node.Name == "IFactory");
        nodes.Should().Contain(node => node.Type == "Method" && node.Name == "Create()");
        edges.Should().Contain(edge =>
            edge.SourceId == "Project::Interface::Demo.IFactory"
            && edge.TargetId == "Project::Method::Demo.IFactory::Create()"
            && edge.RelationshipType == "Contains");
    }

    [Fact]
    public void NewerTypeDeclarations_AreIndexedWithDistinctKinds()
    {
        const string source = """
            namespace Demo;

            public struct Point;
            public record class Person(string Name);
            public record struct Size(int Width);
            public delegate void Notifier(string message);
            """;

        var nodes = ExtractNodes(source, "src/Types.cs");

        nodes.Should().Contain(node => node.Type == "Struct" && node.Name == "Point");
        nodes.Should().Contain(node => node.Type == "Class" && node.Name == "Person");
        nodes.Should().Contain(node => node.Type == "Struct" && node.Name == "Size");
        nodes.Should().Contain(node => node.Type == "Delegate" && node.Name == "Notifier");
    }

    [Fact]
    public void FieldEventIndexerAndOperatorMembers_AreIndexed()
    {
        const string source = """
            namespace Demo;

            public struct Point
            {
                public int X;
                public event System.EventHandler? Changed;

                public event System.EventHandler? ExplicitChanged
                {
                    add { }
                    remove { }
                }

                public int this[int index] => index;
                public static Point operator +(Point left, Point right) => left;
                public static explicit operator int(Point value) => value.X;
            }
            """;

        var nodes = ExtractNodes(source, "src/Point.cs");

        nodes.Should().Contain(node => node.Type == "Field" && node.Name == "X");
        nodes.Should().Contain(node => node.Type == "Event" && node.Name == "Changed");
        nodes.Should().Contain(node => node.Type == "Event" && node.Name == "ExplicitChanged");
        nodes.Should().Contain(node => node.Type == "Indexer" && node.Name == "this(int)");
        nodes.Should().Contain(node => node.Type == "Operator" && node.Name == "operator +(Point,Point)");
        nodes.Should().Contain(node => node.Type == "Operator" && node.Name == "operator explicit(Point)");
    }

    [Fact]
    public void TypeAndMemberDeclarations_AreIndexedForNewerCSharpSyntaxKinds()
    {
        const string source = """
            namespace Demo;

            public struct Point
            {
                public int X;
                public event System.EventHandler? Changed;

                public event System.EventHandler? ExplicitChanged
                {
                    add { Raise(); }
                    remove { }
                }

                public int this[int index] => index;

                public static Point operator +(Point left, Point right) => left;

                public static explicit operator int(Point value) => value.X;

                public void Raise()
                {
                    Helper();
                }

                private void Helper()
                {
                }
            }

            public record class Person(string Name);
            public record struct Size(int Width);
            public delegate void Notifier(string message);

            public interface IFactory
            {
                static abstract Point Create();
            }
            """;

        var (nodes, edges) = ExtractGraph(source, "src/Features.cs");

        nodes.Should().Contain(node => node.Type == "Struct" && node.Name == "Point");
        nodes.Should().Contain(node => node.Type == "Field" && node.Name == "X");
        nodes.Should().Contain(node => node.Type == "Event" && node.Name == "Changed");
        nodes.Should().Contain(node => node.Type == "Event" && node.Name == "ExplicitChanged");
        nodes.Should().Contain(node => node.Type == "Indexer" && node.Name == "this(int)");
        nodes.Should().Contain(node => node.Type == "Operator" && node.Name == "operator +(Point,Point)");
        nodes.Should().Contain(node => node.Type == "Operator" && node.Name == "operator explicit(Point)");
        nodes.Should().Contain(node => node.Type == "Class" && node.Name == "Person");
        nodes.Should().Contain(node => node.Type == "Struct" && node.Name == "Size");
        nodes.Should().Contain(node => node.Type == "Delegate" && node.Name == "Notifier");
        nodes.Should().Contain(node => node.Type == "Method" && node.Name == "Create()");
    }

    [Fact]
    public void TopLevelLocalFunctions_WithSameSignatureInDifferentFiles_GetStableDistinctIds()
    {
        const string source = """
            bool IsAuthorized(HttpRequest request, string expectedApiKey) => true;
            """;

        var firstNodes = ExtractNodes(source, "src/McpServer/Program.cs");
        var secondNodes = ExtractNodes(source, "src/Api/Program.cs");

        var firstMethod = firstNodes.Single(n => n.Type == "Method" && n.Name == "IsAuthorized(HttpRequest,string)");
        var secondMethod = secondNodes.Single(n => n.Type == "Method" && n.Name == "IsAuthorized(HttpRequest,string)");

        firstMethod.Id.Should().Be("Project::Method::File::src/McpServer/Program.cs::IsAuthorized(HttpRequest,string)");
        secondMethod.Id.Should().Be("Project::Method::File::src/Api/Program.cs::IsAuthorized(HttpRequest,string)");
        firstMethod.Id.Should().NotBe(secondMethod.Id);
    }

    [Fact]
    public void TopLevelLocalFunction_IsContainedByItsFileNode()
    {
        const string source = """
            void Configure() { }
            """;

        var nodes = new List<IngestNodeRequest>();
        var edges = new List<IngestEdgeRequest>();
        var root = CSharpSyntaxTree.ParseText(source, path: "src/App/Program.cs").GetCompilationUnitRoot();

        new CSharpAstWalker("src/App/Program.cs", "Project", nodes, edges).Visit(root);

        edges.Should().Contain(e =>
            e.SourceId == "Project::File::src/App/Program.cs"
            && e.TargetId == "Project::Method::File::src/App/Program.cs::Configure()"
            && e.RelationshipType == "Contains");
    }

    [Fact]
    public void MethodNodes_CaptureBoundedSourceSnippet()
    {
        const string source = """
            namespace Shop;

            public sealed class OrderService
            {
                public void PlaceOrder()
                {
                    ValidateOrder();
                }
            }
            """;

        var nodes = ExtractNodes(source, "src/Orders/OrderService.cs");

        var method = nodes.Single(n => n.Type == "Method" && n.Name == "PlaceOrder()");

        method.SourceSnippet.Should().Contain("public void PlaceOrder()");
        method.SourceSnippet.Should().Contain("ValidateOrder();");
    }

    [Fact]
    public void InterfaceAndImplementationMembers_WithSameSignature_GetDistinctIds()
    {
        const string source = """
            namespace Demo;

            public interface IService
            {
                void Run();
            }

            public sealed class Service : IService
            {
                public void Run()
                {
                }
            }
            """;

        var nodes = ExtractNodes(source, "src/Service.cs");

        nodes.Should().Contain(node => node.Type == "Method" && node.Id == "Project::Method::Demo.IService::Run()");
        nodes.Should().Contain(node => node.Type == "Method" && node.Id == "Project::Method::Demo.Service::Run()");
        nodes
            .Where(node => node.Type == "Method" && node.Name == "Run()")
            .Select(node => node.Id)
            .Should()
            .OnlyHaveUniqueItems();
    }

    private static List<IngestNodeRequest> ExtractNodes(string source, string filePath)
    {
        var nodes = new List<IngestNodeRequest>();
        var edges = new List<IngestEdgeRequest>();
        var root = CSharpSyntaxTree.ParseText(source, path: filePath).GetCompilationUnitRoot();

        new CSharpAstWalker(filePath, "Project", nodes, edges).Visit(root);

        return nodes;
    }

    private static (List<IngestNodeRequest> Nodes, List<IngestEdgeRequest> Edges) ExtractGraph(string source, string filePath)
    {
        var nodes = new List<IngestNodeRequest>();
        var edges = new List<IngestEdgeRequest>();
        var root = CSharpSyntaxTree.ParseText(source, path: filePath).GetCompilationUnitRoot();

        new CSharpAstWalker(filePath, "Project", nodes, edges).Visit(root);

        return (nodes, edges);
    }
}
