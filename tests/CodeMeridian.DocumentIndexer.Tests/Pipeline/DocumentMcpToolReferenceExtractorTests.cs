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

    [Fact]
    public void BuildMcpToolFileMap_IndexesToolAttributesAcrossFiles()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteFile("src/McpServer/Tools/CodebaseTools.cs", """
            [McpServerTool(Name = "find_connection")]
            public Task<string> FindConnectionAsync() => Task.FromResult(string.Empty);
            """);
        workspace.WriteFile("src/McpServer/Tools/ExtraTools.cs", """
            [McpServerTool(Name = "find_connection")]
            [McpServerTool(Name = "search_documentation")]
            public Task<string> SearchAsync() => Task.FromResult(string.Empty);
            """);

        var result = DocumentMcpToolReferenceExtractor.BuildMcpToolFileMap(workspace.Root.FullName, "Demo");

        result["find_connection"].Should().BeEquivalentTo([
            "Demo:File:src/McpServer/Tools/CodebaseTools.cs",
            "Demo::File::src/McpServer/Tools/CodebaseTools.cs",
            "Demo:File:src/McpServer/Tools/ExtraTools.cs",
            "Demo::File::src/McpServer/Tools/ExtraTools.cs"
        ]);
        result["search_documentation"].Should().Contain("Demo::File::src/McpServer/Tools/ExtraTools.cs");
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"codemeridian-doc-tools-{Guid.NewGuid():N}"));
        }

        public DirectoryInfo Root { get; }

        public void WriteFile(string relativePath, string content)
        {
            var file = new FileInfo(Path.Combine(Root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            file.Directory!.Create();
            File.WriteAllText(file.FullName, content);
        }

        public void Dispose()
        {
            if (Root.Exists)
                Root.Delete(recursive: true);
        }
    }
}
