using Tandem.Advanced;

namespace Cadence;

public static class PlannerPolicies
{
    public static OutputAcceptancePolicy<CadenceState, PlannerDecision> DecisionBoundaries() =>
        observation =>
        {
            var problems = new List<StructuredOutputProblem>();
            if (observation.Tools.All(tool => tool.Evidence != ToolEvidence.RepositoryInspection))
            {
                problems.Add(
                    new StructuredOutputProblem(
                        "$evidenceUsed",
                        "Your role requires establishing whether the proposed engineering direction is sufficient for the complete packet. You have not examined any repository evidence in this consultation, so you cannot yet have established that outcome. Examine the repository evidence needed to make the decision, then return the decision."
                    )
                );
            }
            if (IsRuntimeCapabilityEscalation(observation.Output))
            {
                problems.Add(
                    new StructuredOutputProblem(
                        "$humanDecisionDomain",
                        "Repository command and runtime capability availability are engineering facts, not a Human Permissions decision. Proceed or revise the approach using the current packet-authorized commands."
                    )
                );
            }
            return problems;
        };

    private static bool IsRuntimeCapabilityEscalation(PlannerDecision decision)
    {
        if (
            decision.Decision != PlannerDecisionValue.NeedsHuman
            || decision.HumanDecisionDomain != HumanDecisionDomain.Permissions
        )
        {
            return false;
        }

        var text = string.Join(
            " ",
            decision.Rationale,
            decision.SafeNextAction,
            decision.HumanQuestion
        );
        return ContainsAny(text, "command", "capabilit", "tool")
            && ContainsAny(text, "authoriz", "expos", "provide", "availab");
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
