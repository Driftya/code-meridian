using CodeMeridian.Evolution.Domain.Cognition;

namespace CodeMeridian.Evolution.Application.Cognition;

public sealed record AffectStimulusRequest(
    string Actor,
    string Source,
    string Reason,
    string ProjectId,
    AffectStimulus Stimulus,
    string IdempotencyKey);

