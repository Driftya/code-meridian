using System.Globalization;
using CodeMeridian.Evolution.Domain.Cognition;
using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Application.Cognition;

internal static class CognitiveStatePostingFactory
{
    public static IReadOnlyList<LedgerPosting> Create(
        AffectState affect,
        IEnumerable<DriveState> drives,
        string provenance)
    {
        ArgumentNullException.ThrowIfNull(affect);
        ArgumentNullException.ThrowIfNull(drives);
        ArgumentException.ThrowIfNullOrWhiteSpace(provenance);
        affect.Validate();
        var postings = new List<LedgerPosting>
        {
            Affect("valence", affect.Valence, provenance),
            Affect("arousal", affect.Arousal, provenance),
            Affect("dopamine", affect.Dopamine, provenance),
            Affect("curiosity", affect.Curiosity, provenance),
            Affect("fatigue", affect.Fatigue, provenance),
            Affect("frustration", affect.Frustration, provenance)
        };

        foreach (var drive in drives)
        {
            drive.Validate();
            postings.Add(new LedgerPosting(
                LedgerAccount.Drive,
                drive.Kind.ToString().ToLowerInvariant(),
                drive.Activation.ToString(CultureInfo.InvariantCulture),
                provenance,
                1m,
                ReconciliationState.Reconciled));
        }

        return Array.AsReadOnly(postings.ToArray());
    }

    private static LedgerPosting Affect(
        string subjectId,
        decimal value,
        string provenance)
    {
        return new LedgerPosting(
            LedgerAccount.Affect,
            subjectId,
            value.ToString(CultureInfo.InvariantCulture),
            provenance,
            1m,
            ReconciliationState.Reconciled);
    }
}

