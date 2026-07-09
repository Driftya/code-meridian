using CodeMeridian.DocumentIndexer.Pipeline;
using FluentAssertions;

namespace CodeMeridian.DocumentIndexer.Tests.Pipeline;

public sealed class DocumentRouteReferenceExtractorTests
{
    [Fact]
    public void ExtractRouteReferences_NormalizesTypedRouteMentions()
    {
        var content = "POST /api/orders/{id:int}?expand=items";

        var result = DocumentRouteReferenceExtractor.ExtractRouteReferences(content, "CodeMeridian");

        result.Should().ContainSingle("CodeMeridian::ApiEndpoint::POST /api/orders/{param}");
    }

    [Fact]
    public void ExtractRouteReferences_NormalizesPercentEncodedTypedRouteMentions()
    {
        var content = "POST /api/orders/%7Bid:int%7D%3Fexpand=items";

        var result = DocumentRouteReferenceExtractor.ExtractRouteReferences(content, "CodeMeridian");

        result.Should().ContainSingle("CodeMeridian::ApiEndpoint::POST /api/orders/{param}");
    }

    [Theory]
    [InlineData("https://example.com/API//Orders/:id?expand=items", "/api/orders/{param}")]
    [InlineData("/API/%257Bid%253Aint%257D#fragment", "/api/{param}")]
    [InlineData("orders", "/orders")]
    public void NormalizeRouteTemplate_HandlesAbsoluteEncodedAndColonSyntax(string route, string expected)
    {
        DocumentRouteReferenceExtractor.NormalizeRouteTemplate(route).Should().Be(expected);
    }
}
