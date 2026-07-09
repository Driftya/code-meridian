using CodeMeridian.Sdk.Models;
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

    [Fact]
    public void AgentModels_ExposeConfiguredProperties()
    {
        var request = new AgentRequest
        {
            Query = "Find diagnostics",
            ProjectContext = "CodeMeridian",
            RequiredCapabilities = ["diagnostics"],
            Metadata = new Dictionary<string, object?> { ["source"] = "test" }
        };
        var response = new AgentResponse
        {
            Content = "Done",
            AgentName = "Payments",
            Sources = ["docs/plan.md"],
            Metadata = new Dictionary<string, object?> { ["kind"] = "summary" },
            IsSuccess = false,
            ErrorMessage = "boom"
        };

        request.Query.Should().Be("Find diagnostics");
        request.RequiredCapabilities.Should().ContainSingle("diagnostics");
        request.Metadata["source"].Should().Be("test");
        response.AgentName.Should().Be("Payments");
        response.Sources.Should().ContainSingle("docs/plan.md");
        response.IsSuccess.Should().BeFalse();
        response.ErrorMessage.Should().Be("boom");
    }
}
