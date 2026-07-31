namespace CodeMeridian.Evolution.Domain.Cognition;

public sealed record AffectStimulus(
    decimal Reward,
    decimal Novelty,
    decimal PredictionError,
    decimal Effort,
    decimal Threat)
{
    public void Validate()
    {
        ValidateRange(Reward, -1m, 1m, nameof(Reward));
        ValidateRange(Novelty, 0m, 1m, nameof(Novelty));
        ValidateRange(PredictionError, 0m, 1m, nameof(PredictionError));
        ValidateRange(Effort, 0m, 1m, nameof(Effort));
        ValidateRange(Threat, 0m, 1m, nameof(Threat));
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

