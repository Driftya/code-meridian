using CodeMeridian.Evolution.Application.Reasoning;
using CodeMeridian.Evolution.Infrastructure.Reasoning;

namespace CodeMeridian.Evolution.Infrastructure.Tests.Reasoning;

public sealed class FakeReasoningProviderTests
{
    [Fact]
    public async Task InvokeWithoutEvidenceAbstains()
    {
        var provider = new FakeReasoningProvider();
        var result = await provider.InvokeAsync(
            new ReasoningRequest(
                Guid.NewGuid(),
                provider.Id,
                "critic",
                "Assess the evidence.",
                [],
                200,
                TimeSpan.FromSeconds(1),
                "reasoning:test"),
            CancellationToken.None);

        Assert.True(result.Abstained);
        Assert.Equal(1m, result.Uncertainty);
    }
}
