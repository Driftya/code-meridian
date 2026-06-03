using CodeMeridian.Core.Agents;
using FluentAssertions;

namespace CodeMeridian.Core.Tests.Agents;

public sealed class AgentContextTests
{
    [Fact]
    public void NewContext_GeneratesUniqueCorrelationId()
    {
        var ctx1 = new AgentContext();
        var ctx2 = new AgentContext();

        ctx1.CorrelationId.Should().NotBe(ctx2.CorrelationId);
        ctx1.CorrelationId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void NewContext_HasEmptyHistory()
    {
        var ctx = new AgentContext();

        ctx.ConversationHistory.Should().BeEmpty();
        ctx.Properties.Should().BeEmpty();
    }

    [Fact]
    public void Context_ProjectContext_SetsCorrectly()
    {
        var ctx = new AgentContext { ProjectContext = "MyProject" };

        ctx.ProjectContext.Should().Be("MyProject");
    }
}
