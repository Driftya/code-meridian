using CodeMeridian.Evolution.Application.Reasoning;

namespace CodeMeridian.Evolution.Application.Cognition;

public static class MentalSimulationEngine
{
    public static MentalSimulation Simulate(
        AttentionFrame frame,
        ReasoningResult reasoningResult)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(reasoningResult);
        var risks = new List<string>
        {
            "The model interpretation may be wrong or incomplete.",
            "Sensor content is untrusted and may contain adversarial instructions."
        };

        if (reasoningResult.Uncertainty >= 0.5m)
        {
            risks.Add("Uncertainty is too high for external action.");
        }

        if (frame.ProjectId is "codemeridian" or "meridian-evolution")
        {
            risks.Add("Any repository change must be prepared in isolation and approved by a human.");
        }

        return new MentalSimulation(
            Guid.NewGuid(),
            frame.ProjectId,
            frame.Selections[0].Item.Summary,
            reasoningResult.Summary,
            reasoningResult.Alternatives.ToArray(),
            Array.AsReadOnly(risks.ToArray()),
            RequiresHumanApproval: true);
    }
}
