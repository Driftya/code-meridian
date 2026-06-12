using CodeMeridian.RoslynIndexer.Pipeline;
using FluentAssertions;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class RouteTemplateNormalizerTests
{
    [Theory]
    [InlineData("https://example.test/api/orders/", "/api/orders")]
    [InlineData("/api/orders/{id:int}?draft=true#summary", "/api/orders/{param}")]
    [InlineData(@"api\orders//{id}", "/api/orders/{param}")]
    public void Normalize_HandlesAbsoluteAndRelativeRouteForms(string input, string expected)
    {
        RouteTemplateNormalizer.Normalize(input).Should().Be(expected);
    }
}
