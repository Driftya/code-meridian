using CodeMeridian.DocumentIndexer.Pipeline;
using FluentAssertions;

namespace CodeMeridian.DocumentIndexer.Tests.Pipeline;

public sealed class DocumentCodeReferenceExtractorTests
{
    [Fact]
    public void ExtractCodeFileReferences_ResolvesMarkdownAndInlinePaths()
    {
        var content = """
            See [gateway](../src/Payments/PaymentGateway.cs) and `src/Orders/OrderService.ts`.
            """;

        var result = DocumentCodeReferenceExtractor.ExtractCodeFileReferences(content, "DemoProject", "docs/architecture.md");

        result.Should().BeEquivalentTo([
            "DemoProject:File:src/Payments/PaymentGateway.cs",
            "DemoProject::File::src/Payments/PaymentGateway.cs",
            "DemoProject:File:src/Orders/OrderService.ts",
            "DemoProject::File::src/Orders/OrderService.ts"
        ]);
    }
}
