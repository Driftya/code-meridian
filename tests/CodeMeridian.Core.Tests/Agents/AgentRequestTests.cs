using CodeMeridian.Core.Agents;
using FluentAssertions;

namespace CodeMeridian.Core.Tests.Agents;

public sealed class AgentRequestTests
{
    [Fact]
    public void Request_RequiredCapabilities_DefaultsToNull()
    {
        var request = new AgentRequest { Query = "test" };

        request.RequiredCapabilities.Should().BeNull();
        request.ProjectContext.Should().BeNull();
        request.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void Request_WithCapabilities_FiltersCorrectly()
    {
        var request = new AgentRequest
        {
            Query = "find all callers of UserService",
            ProjectContext = "MyApi",
            RequiredCapabilities = ["call-graph", "code-structure"]
        };

        request.RequiredCapabilities.Should().HaveCount(2);
        request.RequiredCapabilities.Should().Contain("call-graph");
    }
}
