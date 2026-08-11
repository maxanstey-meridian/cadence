using Tandem.Advanced;

namespace Cadence;

public static class PlannerPolicies
{
    public static OutputAcceptancePolicy<CadenceState, PlannerDecision> RepositoryGrounded() =>
        observation =>
        {
            var request = observation.Context.State.ExecutorTransition
                is ExecutorTransition.PlannerRequested requested
                ? requested.Request
                : null;
            if (
                observation.Output.Decision == PlannerDecisionValue.Reorient
                && request?.QuestionType != PlannerQuestionType.SessionReliability
            )
            {
                return
                [
                    new StructuredOutputProblem(
                        "$reorient",
                        "Reorient is valid only for a SessionReliability request."
                    ),
                ];
            }

            return
                observation.Output.Decision is PlannerDecisionValue.NeedsHuman
                || observation.Tools.Any(tool => tool.Evidence == ToolEvidence.RepositoryInspection)
                ? []
                :
                [
                    new StructuredOutputProblem(
                        "$grounding",
                        "Proceed, ProceedWithConstraints, ReviseApproach, Reorient, and Stop require repository inspection in this consult. "
                            + "Use an available read-only repository tool to verify the material files and seams, "
                            + "then return only the corrected JSON decision with concrete evidenceUsed entries."
                    ),
                ];
        };
}
