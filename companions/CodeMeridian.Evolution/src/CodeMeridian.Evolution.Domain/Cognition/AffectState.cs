namespace CodeMeridian.Evolution.Domain.Cognition;

public sealed record AffectState(
    decimal Valence,
    decimal Arousal,
    decimal Dopamine,
    decimal Curiosity,
    decimal Fatigue,
    decimal Frustration,
    DateTimeOffset UpdatedAt)
{
    public static AffectState Baseline(DateTimeOffset at)
    {
        return new AffectState(
            Valence: 0m,
            Arousal: 0.2m,
            Dopamine: 0.25m,
            Curiosity: 0.45m,
            Fatigue: 0.1m,
            Frustration: 0m,
            at);
    }

    public void Validate()
    {
        ValidateRange(Valence, -1m, 1m, nameof(Valence));
        ValidateRange(Arousal, 0m, 1m, nameof(Arousal));
        ValidateRange(Dopamine, 0m, 1m, nameof(Dopamine));
        ValidateRange(Curiosity, 0m, 1m, nameof(Curiosity));
        ValidateRange(Fatigue, 0m, 1m, nameof(Fatigue));
        ValidateRange(Frustration, 0m, 1m, nameof(Frustration));
    }

    private static void ValidateRange(
        decimal value,
        decimal minimum,
        decimal maximum,
        string parameterName)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Value must be between {minimum} and {maximum}.");
        }
    }
}

