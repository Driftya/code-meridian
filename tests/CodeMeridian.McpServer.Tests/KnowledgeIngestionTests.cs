using System.Reflection;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.McpServer.Api;
using CodeMeridian.McpServer.Tools;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.McpServer.Tests;

public sealed class KnowledgeIngestionTests
{
    [Fact]
    public async Task KnowledgeTools_IngestDocumentAsync_ForwardsWeakMentionMetadata()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        KnowledgeDocument? captured = null;

        vector
            .UpsertAsync(Arg.Do<KnowledgeDocument>(doc => captured = doc), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = new KnowledgeTools(graph, vector);

        await sut.IngestDocumentAsync(
            content: "Implementation notes",
            source: "docs/notes.md",
            projectContext: "CodeMeridian",
            id: "doc-2",
            relatedNodeIdsCsv: "Class:CodeMeridian.McpServer.Tools.CodebaseTools");

        captured.Should().NotBeNull();
        captured!.Metadata.Should().ContainKey("relatedNodeIds");
        captured.Metadata["relatedNodeIds"].Should().Be("Class:CodeMeridian.McpServer.Tools.CodebaseTools");
        captured.ProjectContext.Should().Be("CodeMeridian");
    }

    [Fact]
    public void KnowledgeApiEndpoints_ParseMetadata_StoresRelatedNodeIdsAsPrimitiveProperty()
    {
        var method = typeof(KnowledgeApiEndpoints).GetMethod(
            "ParseMetadata",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();

        var result = (Dictionary<string, string>)method!.Invoke(null, new object?[] { "Method:Foo.Bar,Method:Baz.Qux" })!;

        result.Should().ContainKey("relatedNodeIds");
        result["relatedNodeIds"].Should().Be("Method:Foo.Bar,Method:Baz.Qux");
    }
}
