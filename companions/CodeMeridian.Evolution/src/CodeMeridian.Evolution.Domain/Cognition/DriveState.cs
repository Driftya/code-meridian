namespace CodeMeridian.Evolution.Domain.Cognition;

public sealed record DriveState(
    DriveKind Kind,
    decimal Activation,
    DateTimeOffset UpdatedAt)
{
    public void Validate()
    {
        if (Activation is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Activation),
                Activation,
                "Drive activation must be between zero and one.");
        }
    }
}

