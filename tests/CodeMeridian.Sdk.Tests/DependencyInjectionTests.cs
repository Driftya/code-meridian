using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace CodeMeridian.Sdk.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddCodeMeridianClient_ConfiguresBaseAddressHeadersTimeoutAndBearerToken()
    {
        var services = new ServiceCollection();

        services.AddCodeMeridianClient("https://codemeridian.example", "secret-key");

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient(nameof(CodeMeridianClient));

        client.BaseAddress.Should().Be(new Uri("https://codemeridian.example/"));
        client.Timeout.Should().Be(TimeSpan.FromMinutes(10));
        client.DefaultRequestHeaders.Accept.Single().MediaType.Should().Be("application/json");
        client.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");
        client.DefaultRequestHeaders.Authorization.Parameter.Should().Be("secret-key");
    }
}
