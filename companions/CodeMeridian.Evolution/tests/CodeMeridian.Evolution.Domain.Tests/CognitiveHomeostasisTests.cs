using CodeMeridian.Evolution.Domain.Cognition;

namespace CodeMeridian.Evolution.Domain.Tests;

public sealed class CognitiveHomeostasisTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse(
        "2026-07-28T12:00:00Z",
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void NovelPositiveOutcomeRaisesDopamineAndCuriosityWithinBounds()
    {
        var baseline = AffectState.Baseline(At);

        var updated = CognitiveHomeostasis.Apply(
            baseline,
            new AffectStimulus(
                Reward: 0.8m,
                Novelty: 0.9m,
                PredictionError: 0.5m,
                Effort: 0.2m,
                Threat: 0m),
            At.AddMinutes(1));

        Assert.True(updated.Dopamine > baseline.Dopamine);
        Assert.True(updated.Curiosity > baseline.Curiosity);
        Assert.InRange(updated.Valence, -1m, 1m);
        Assert.All(
            CognitiveHomeostasis.DeriveDrives(updated),
            drive => Assert.InRange(drive.Activation, 0m, 1m));
    }

    [Fact]
    public void NegativeOutcomeRaisesFrustration()
    {
        var baseline = AffectState.Baseline(At);

        var updated = CognitiveHomeostasis.Apply(
            baseline,
            new AffectStimulus(
                Reward: -0.8m,
                Novelty: 0m,
                PredictionError: 0.6m,
                Effort: 0.7m,
                Threat: 0.5m),
            At);

        Assert.True(updated.Frustration > baseline.Frustration);
        Assert.True(updated.Valence < baseline.Valence);
    }

    [Fact]
    public void StateDecaysTowardBaseline()
    {
        var elevated = new AffectState(
            0.8m,
            0.9m,
            0.9m,
            0.9m,
            0.8m,
            0.7m,
            At);

        var decayed = CognitiveHomeostasis.Decay(elevated, At.AddHours(24));
        var baseline = AffectState.Baseline(At.AddHours(24));

        Assert.True(Math.Abs(decayed.Dopamine - baseline.Dopamine) <
                    Math.Abs(elevated.Dopamine - baseline.Dopamine));
        Assert.True(Math.Abs(decayed.Curiosity - baseline.Curiosity) <
                    Math.Abs(elevated.Curiosity - baseline.Curiosity));
        Assert.True(decayed.Frustration < elevated.Frustration);
    }
}
