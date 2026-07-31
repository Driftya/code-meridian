using CodeMeridian.Evolution.Application.Projections;
using CodeMeridian.Evolution.Domain.Governance;

namespace CodeMeridian.Evolution.Application.Tests;

public sealed class CognitiveProjectionServiceTests
{
    [Fact]
    public void EmptyJournalProducesSafeDefaultProjection()
    {
        var generatedAt = DateTimeOffset.Parse(
            "2026-07-28T12:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture);

        var snapshot = CognitiveProjectionService.Rebuild([], generatedAt);

        Assert.Equal(generatedAt, snapshot.GeneratedAt);
        Assert.Equal(AutonomyLevel.Recommend, snapshot.AutonomyLevel);
        Assert.True(snapshot.IsBalanced);
        Assert.Empty(snapshot.ActiveGoals);
        Assert.Empty(snapshot.Attention);
    }
}
