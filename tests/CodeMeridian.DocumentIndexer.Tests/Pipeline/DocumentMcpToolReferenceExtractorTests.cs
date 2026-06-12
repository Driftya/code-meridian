using CodeMeridian.DocumentIndexer.Pipeline;
using FluentAssertions;

namespace CodeMeridian.DocumentIndexer.Tests.Pipeline;

public sealed class DocumentMcpToolReferenceExtractorTests
{
    [Fact]
    public void ExtractMcpToolReferences_ExpandsToolNameToFileNodeIds()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["find_connection"] = ["Demo:File:src/McpServer/Tools/CodebaseTools.Analytics.cs", "Demo::File::src/McpServer/Tools/CodebaseTools.Analytics.cs"]
        };

        var result = DocumentMcpToolReferenceExtractor.ExtractMcpToolReferences(
            "[McpServerTool(Name = \"find_connection\")]",
            map);

        result.Should().BeEquivalentTo(map["find_connection"]);
    }
}
