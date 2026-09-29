using Tandem.Advanced;

namespace Cadence;

public static class PlannerPolicies
{
    public static OutputAcceptancePolicy<CadenceState, PlannerDecision> DecisionBoundaries() =>
        observation =>
        {
            var problems = new List<ValidationProblem>();
            if (observation.Tools.All(tool => tool.Evidence != ToolEvidence.RepositoryInspection))
            {
                problems.Add(
                    new ValidationProblem(
                        "$evidenceUsed",
                        "You have not examined repository evidence in this consultation. Inspect the facts needed to test the proposed approach and its material premises against the applicable packet requirements. Use those facts to assess relevant omissions, contradictions, and unsupported progress claims before returning your decision. Keep the investigation bounded to facts that could change that decision."
                    )
                );
            }
            return problems;
        };
}
