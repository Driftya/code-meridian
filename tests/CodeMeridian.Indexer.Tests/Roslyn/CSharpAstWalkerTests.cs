using CodeMeridian.RoslynIndexer.Pipeline;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeMeridian.Indexer.Tests.Roslyn;

public sealed class CSharpAstWalkerTests
{
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

    private static List<IngestNodeRequest> ExtractNodes(string source, string filePath)
    {
        var nodes = new List<IngestNodeRequest>();
        var edges = new List<IngestEdgeRequest>();
        var root = CSharpSyntaxTree.ParseText(source, path: filePath).GetCompilationUnitRoot();

        new CSharpAstWalker(filePath, "Project", nodes, edges).Visit(root);

        return nodes;
    }
}
