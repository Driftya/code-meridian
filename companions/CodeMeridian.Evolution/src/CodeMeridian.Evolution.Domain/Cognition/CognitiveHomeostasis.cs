namespace CodeMeridian.Evolution.Domain.Cognition;

public static class CognitiveHomeostasis
{
    public static AffectState Decay(AffectState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();

        if (now <= state.UpdatedAt)
        {
            return state;
        }

        var elapsedHours = Math.Min((now - state.UpdatedAt).TotalHours, 168d);
        var baseline = AffectState.Baseline(now);
        return new AffectState(
            Approach(state.Valence, baseline.Valence, elapsedHours, 6d),
            Approach(state.Arousal, baseline.Arousal, elapsedHours, 4d),
            Approach(state.Dopamine, baseline.Dopamine, elapsedHours, 2d),
            Approach(state.Curiosity, baseline.Curiosity, elapsedHours, 8d),
            Approach(state.Fatigue, baseline.Fatigue, elapsedHours, 6d),
            Approach(state.Frustration, baseline.Frustration, elapsedHours, 3d),
            now);
    }

    public static AffectState Apply(
        AffectState state,
        AffectStimulus stimulus,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(stimulus);
        stimulus.Validate();
        var current = Decay(state, occurredAt);
        var positiveReward = Math.Max(stimulus.Reward, 0m);
        var negativeReward = Math.Max(-stimulus.Reward, 0m);

        var updated = new AffectState(
            ClampSigned(
                current.Valence +
                (stimulus.Reward * 0.35m) -
                (stimulus.Threat * 0.25m)),
            Clamp01(
                current.Arousal +
                (stimulus.Novelty * 0.18m) +
                (stimulus.PredictionError * 0.2m) +
                (stimulus.Threat * 0.35m) -
                0.05m),
            Clamp01(
                current.Dopamine +
                (positiveReward * 0.4m) +
                (stimulus.Novelty * 0.18m) +
                (stimulus.PredictionError * 0.22m) -
                (negativeReward * 0.2m)),
            Clamp01(
                current.Curiosity +
                (stimulus.Novelty * 0.35m) +
                (stimulus.PredictionError * 0.25m) -
                (stimulus.Threat * 0.15m) -
                (current.Fatigue * 0.1m)),
            Clamp01(
                current.Fatigue +
                (stimulus.Effort * 0.3m) +
                (current.Arousal * 0.04m) -
                0.04m),
            Clamp01(
                current.Frustration +
                (negativeReward * 0.4m) +
                (stimulus.Threat * 0.25m) +
                (stimulus.Effort * 0.08m) -
                (positiveReward * 0.2m)),
            occurredAt);
        updated.Validate();
        return updated;
    }

    public static IReadOnlyList<DriveState> DeriveDrives(AffectState affect)
    {
        ArgumentNullException.ThrowIfNull(affect);
        affect.Validate();

        return Array.AsReadOnly(
        [
            new DriveState(
                DriveKind.Curiosity,
                Clamp01(
                    (affect.Curiosity * 0.7m) +
                    (affect.Dopamine * 0.3m) -
                    (affect.Fatigue * 0.25m)),
                affect.UpdatedAt),
            new DriveState(
                DriveKind.Competence,
                Clamp01(0.3m + (affect.Frustration * 0.35m) + (affect.Dopamine * 0.2m)),
                affect.UpdatedAt),
            new DriveState(
                DriveKind.Coherence,
                Clamp01(0.45m + (affect.Frustration * 0.3m)),
                affect.UpdatedAt),
            new DriveState(
                DriveKind.Safety,
                Clamp01(0.2m + (affect.Arousal * 0.35m) + (affect.Frustration * 0.25m)),
                affect.UpdatedAt),
            new DriveState(
                DriveKind.Connection,
                Clamp01(0.2m + (Math.Max(affect.Valence, 0m) * 0.15m)),
                affect.UpdatedAt),
            new DriveState(
                DriveKind.Rest,
                affect.Fatigue,
                affect.UpdatedAt)
        ]);
    }

    private static decimal Approach(
        decimal value,
        decimal baseline,
        double elapsedHours,
        double halfLifeHours)
    {
        var remaining = Math.Pow(0.5d, elapsedHours / halfLifeHours);
        return baseline + ((value - baseline) * (decimal)remaining);
    }

    private static decimal Clamp01(decimal value)
    {
        return Math.Clamp(value, 0m, 1m);
    }

    private static decimal ClampSigned(decimal value)
    {
        return Math.Clamp(value, -1m, 1m);
    }
}
